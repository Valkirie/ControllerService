using ControllerCommon;
using ControllerCommon.Utils;
using Microsoft.Extensions.Logging;
using SharpDX.XInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Media3D;
using TouchEventSample;
using static HandheldCompanion.OverlayHook;
using static TouchEventSample.TouchSourceWinTouch;
using GamepadButtonFlags = SharpDX.XInput.GamepadButtonFlags;

namespace HandheldCompanion.Views.Windows
{
    /// <summary>
    /// Interaction logic for Overlay.xaml
    /// </summary>
    public partial class Overlay : Window
    {
        private IntPtr hWnd;
        private IntPtr hWinEventHook;
        private Process targetProc = null;

        protected WinEventDelegate WinEventDelegate;
        static GCHandle GCSafetyHandle;
        private bool isHooked = true; // hack

        #region import
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        #endregion

        private ILogger logger;
        private PipeClient pipeClient;

        private Model CurrentModel;
        private Model ProductModel;
        private Model VirtualModel;
        private OverlayModelMode ModelMode;

        private Point OverlayPosition;
        private Point LeftTrackPadPosition;
        private Point RightTrackPadPosition;

        int HapticFeedbackCounterRight;
        int FrequencyRight;
        int FrequencyRightPrev;
        int FrequencyArrayLengthPrev;
        double SpeedRight;
        double SpeedRightPrev;
        double[] SpeedArray = new double[45];
        int SpeedArrayIndex;
        Vibration HapticVibration = new Vibration();
        Timer HapticTimerLeft = new Timer() { Interval = 25 };
        Timer HapticTimerRight = new Timer() { Interval = 25 };

        Dictionary<TouchTarget, double> prevTrackpadSlidingDistance = new();
        Dictionary<TouchTarget, double> TrackpadSlidingDistance = new();

        Timer LeftTrackpadSliding = new Timer() { Interval = 100, AutoReset = true };
        Timer RightTrackpadSliding = new Timer() { Interval = 10, AutoReset = true };
        Dictionary<TouchTarget, Timer> TrackpadSlidingTimer = new();

        private enum TouchTarget
        {
            TrackpadLeft = 1,
            TrackpadRight = 2
        }

        private TouchSourceWinTouch touchsource;
        private long prevLeftTrackPadTime;
        private long prevRightTrackPadTime;
        private TouchTarget target;
        private TouchArgs swipe;

        // Gamepad vars
        private MultimediaTimer UpdateTimer;
        private ControllerEx controllerEx;
        private Gamepad Gamepad;
        private State GamepadState;
        private bool ControllerTriggerListening = false;
        private bool TrackpadsTriggerListening = false;

        private Vector3D FaceCameraObjectAlignment;

        public event ControllerTriggerUpdatedEventHandler ControllerTriggerUpdated;
        public delegate void ControllerTriggerUpdatedEventHandler(GamepadButtonFlags button);

        public event TrackpadsTriggerUpdatedEventHandler TrackpadsTriggerUpdated;
        public delegate void TrackpadsTriggerUpdatedEventHandler(GamepadButtonFlags button);

        private float TriggerAngleShoulderLeft;
        private float TriggerAngleShoulderRight;

        // TODO Dummy variables, placeholder and for testing 
        short MotorLeftPlaceholder;
        short MotorRightPlaceholder;

        public Overlay()
        {
            InitializeComponent();

            // hook vars
            WinEventDelegate = new WinEventDelegate(WinEventCallback);
            GCSafetyHandle = GCHandle.Alloc(WinEventDelegate);

            // touch vars
            touchsource = new TouchSourceWinTouch(this);
            touchsource.Touch += Touchsource_Touch;

            // initialize timers
            UpdateTimer = new MultimediaTimer(10);
            UpdateTimer.Tick += UpdateReport;
            UpdateTimer.Start();

            HapticTimerLeft.Elapsed += HapticTimerLeft_Elapsed;
            HapticTimerRight.Elapsed += HapticTimerRight_Elapsed;

            TrackpadSlidingTimer[TouchTarget.TrackpadLeft] = LeftTrackpadSliding;
            TrackpadSlidingTimer[TouchTarget.TrackpadRight] = RightTrackpadSliding;

            LeftTrackpadSliding.Elapsed += LeftTrackpadSliding_Elapsed;
            RightTrackpadSliding.Elapsed += RightTrackpadSliding_Elapsed;

            this.SourceInitialized += Overlay_SourceInitialized;
        }

        private void RightTrackpadSliding_Elapsed(object? sender, ElapsedEventArgs e)
        {

            int[] FrequencyArray = new int[] { 0 };
            float FractionalPosition = 0.0f;
            FrequencyRight = 0;

            // Calculate speed
            // this is actually distance since last timer elapse ie a speed
            // pixels per time (depends on timer), default 10 msec
            SpeedRight = Math.Abs(TrackpadSlidingDistance[TouchTarget.TrackpadRight] - prevTrackpadSlidingDistance[TouchTarget.TrackpadRight]);
            SpeedRight *= 100.0; // pixels per second      

            // Add to array, loop index
            if (SpeedArrayIndex > SpeedArray.Length - 1) { SpeedArrayIndex = 0; }
            SpeedArray[SpeedArrayIndex] = SpeedRight;
            SpeedArrayIndex += 1;

            // If we have the start of movement,
            // burst fill average array with speed value
            // prevents ramping up
            // Note, alternatively we can compare with previous average, which means multiple cycles of 0
            if (SpeedRightPrev == 0 && SpeedRight > 0)
            {
                for (int i = 0; i < SpeedArray.Length; i++)
                {
                    SpeedArray[i] = SpeedRight;
                }
            }

            // Determine average
            double SpeedAverage = SpeedArray.Average();

            //logger.LogInformation("SpeedAverage: {0}, SpeedRight: {1}, Index {2}, Array: {3}", SpeedAverage, SpeedRight, SpeedArrayIndex, SpeedArray);

            // Todo add something that we don't calculate etc if speed is 0

            if (SpeedAverage > 0) { 

                // Generate frequency array to step through
                // Range speed to frequency
                float InSpeedMin = 1;    // pixels / second
                float InSpeedMax = 600; // pixels / second
                float OutFrequencyMin = 14; // Amount of array elements for vibration off minimal ie longer silence
                float OutFrequencyMax = 4; // Amount of array elements for vibration off minimal ie short silence
                int LengthOfOn = 2; // Amount of array elements for vibration on

                int AmountOfElements = (int)Math.Round((Math.Clamp(SpeedAverage, InSpeedMin, InSpeedMax) - InSpeedMin) * (OutFrequencyMax - OutFrequencyMin) / (InSpeedMax - InSpeedMin) + OutFrequencyMin);
                FrequencyRight = AmountOfElements; // Todo, cleanup

                // Build array
                FrequencyArray = new int[LengthOfOn + AmountOfElements];
                // Fill array
                for (int i = 0; i < LengthOfOn; i++)
                {
                    FrequencyArray[i] = 1;
                }

                for (int i = LengthOfOn; i < FrequencyArray.Length; i++)
                {
                    FrequencyArray[i] = 0;
                }

                //logger.LogInformation("Speed: {0}, AmountOfElementsFloat {1}, FrequencyArray: {2}", SpeedAverage, AmountOfElements, FrequencyArray);

                // When frequency changes, continue from similar
                // fractional position in updated frequency array
                if (FrequencyRight != FrequencyRightPrev) {
                    // Determine position from previous array info
                    FractionalPosition = ((float)HapticFeedbackCounterRight + 1.0f) / (float)FrequencyArrayLengthPrev;
                    // Determine fractional position for current array
                    HapticFeedbackCounterRight = (int)Math.Round((((float)FrequencyArray.Length * FractionalPosition) - 1.0f));
                }

            }

            // Start over from start of array if we go beyond currently selected frequency array
            if (HapticFeedbackCounterRight > FrequencyArray.Length - 1 || HapticFeedbackCounterRight < 0) { HapticFeedbackCounterRight = 0; }

            // Read frequency array
            if (FrequencyArray[HapticFeedbackCounterRight] == 1) { HapticVibration.RightMotorSpeed = 4000; }
            else { HapticVibration.RightMotorSpeed = 0; }
            controllerEx.Controller.SetVibration(HapticVibration);

            //logger.LogInformation("SpeedAverage: {0}, FrequencyRight: {1}, HapticFeedbackCounterRight: {2}, Vibe: {3}, FractionalPosition {4}, FreqArray {5}", SpeedAverage, FrequencyRight, HapticFeedbackCounterRight, FrequencyArray[HapticFeedbackCounterRight], FractionalPosition, FrequencyArray);

            // Increment index counter
            HapticFeedbackCounterRight += 1;
            // Store previous for next round
            prevTrackpadSlidingDistance[TouchTarget.TrackpadRight] = TrackpadSlidingDistance[TouchTarget.TrackpadRight];
            FrequencyRightPrev = FrequencyRight;
            FrequencyArrayLengthPrev = FrequencyArray.Length;
            SpeedRightPrev = SpeedRight;
        }

        private void LeftTrackpadSliding_Elapsed(object? sender, ElapsedEventArgs e)
        {
            double dist = Math.Abs(prevTrackpadSlidingDistance[TouchTarget.TrackpadLeft] - TrackpadSlidingDistance[TouchTarget.TrackpadLeft]);

            if (dist > 10)
                HapticFeedback(TouchTarget.TrackpadLeft);

            prevTrackpadSlidingDistance[TouchTarget.TrackpadLeft] = TrackpadSlidingDistance[TouchTarget.TrackpadLeft];

        }

        private void HapticTimerRight_Elapsed(object? sender, ElapsedEventArgs e)
        {
            //HapticVibration.RightMotorSpeed = 0;
            //controllerEx.Controller.SetVibration(HapticVibration);
        }

        private void HapticTimerLeft_Elapsed(object? sender, ElapsedEventArgs e)
        {
            HapticVibration.LeftMotorSpeed = 0;
            controllerEx.Controller.SetVibration(HapticVibration);
        }

        public Overlay(ILogger logger, PipeClient pipeClient) : this()
        {
            this.logger = logger;

            this.pipeClient = pipeClient;
            this.pipeClient.ServerMessage += OnServerMessage;
        }

        public void UpdateProductModel(Model ProductModel)
        {
            this.ProductModel = ProductModel;
            UpdateModel();
        }

        public void UpdateVirtualModel(Model VirtualModel)
        {
            this.VirtualModel = VirtualModel;
            UpdateModel();
        }

        public void UpdateModelMode(OverlayModelMode ModelMode)
        {
            this.ModelMode = ModelMode;
            UpdateModel();
        }

        public void UpdateModel()
        {
            switch (this.ModelMode)
            {
                case OverlayModelMode.OEM:
                    if (ProductModel != null)
                    {
                        CurrentModel = ProductModel;
                        ModelVisual3D.Content = ProductModel.model3DGroup;
                    }
                    else goto case OverlayModelMode.Virtual;
                    break;
                case OverlayModelMode.Virtual:
                    if (VirtualModel != null)
                    {
                        CurrentModel = VirtualModel;
                        ModelVisual3D.Content = VirtualModel.model3DGroup;
                    }
                    break;
            }

            ModelViewPort.ZoomExtents();
        }

        private void Overlay_SourceInitialized(object? sender, EventArgs e)
        {
            //Set the window style to noactivate.
            WindowInteropHelper helper = new WindowInteropHelper(this);
            SetWindowLong(helper.Handle, GWL_EXSTYLE,
                GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);

            this.LeftTrackPadPosition = LeftTrackpad.PointToScreen(new Point(0, 0));
            this.RightTrackPadPosition = RightTrackpad.PointToScreen(new Point(0, 0));
        }

        private void HapticFeedback(TouchTarget target)
        {
            if (controllerEx is null)
                return;

            switch (target)
            {
                default:
                case TouchTarget.TrackpadLeft:
                    //HapticVibration.LeftMotorSpeed = 4000;
                    HapticTimerLeft.Stop();
                    HapticTimerLeft.Start();
                    break;
                case TouchTarget.TrackpadRight:
                    //HapticVibration.RightMotorSpeed = 4000;
                    HapticTimerRight.Stop();
                    HapticTimerRight.Start();
                    break;
            }

            controllerEx.Controller.SetVibration(HapticVibration);
        }

        private void Touchsource_Touch(TouchArgs args, long time)
        {
            double X = args.LocationX - this.OverlayPosition.X;
            double Y = args.LocationY - this.OverlayPosition.Y;

            double CenterX = this.ActualWidth / 2;
            target = X < CenterX ? TouchTarget.TrackpadLeft : TouchTarget.TrackpadRight;

            CursorButton Button = CursorButton.None;
            Point CurrentPoint;

            switch (target)
            {
                default:
                case TouchTarget.TrackpadLeft:
                    Button = CursorButton.TouchLeft;
                    CurrentPoint = LeftTrackPadPosition;
                    break;
                case TouchTarget.TrackpadRight:
                    Button = CursorButton.TouchRight;
                    CurrentPoint = RightTrackPadPosition;
                    break;
            }

            // normalized
            var relativeX = Math.Clamp(args.LocationX - CurrentPoint.X, 0, LeftTrackpad.ActualWidth);
            var relativeY = Math.Clamp(args.LocationY - CurrentPoint.Y, 0, LeftTrackpad.ActualHeight);

            var normalizedX = (relativeX / LeftTrackpad.ActualWidth) / 2.0d;
            var normalizedY = relativeY / LeftTrackpad.ActualHeight;

            switch (args.Status)
            {
                case CursorEvent.EventType.DOWN:
                    TrackpadSlidingDistance[target] = 0;
                    prevTrackpadSlidingDistance[target] = 0;
                    TrackpadSlidingTimer[target].Start();
                    break;
                case CursorEvent.EventType.MOVE:
                    TrackpadSlidingDistance[target] = Math.Sqrt(relativeX * relativeX + relativeY * relativeY);
                    break;
                case CursorEvent.EventType.UP:
                    TrackpadSlidingTimer[target].Stop();
                    // implement inertia
                    break;
            }

            switch (target)
            {
                default:
                case TouchTarget.TrackpadLeft:
                    {
                        if (args.Status == CursorEvent.EventType.DOWN)
                        {
                            LeftTrackpad.Opacity += 0.25;
                            var elapsed = time - prevLeftTrackPadTime;
                            if (elapsed < 200)
                                args.Flags = 30; // double tap
                            prevLeftTrackPadTime = time;
                        }
                        else if (args.Status == CursorEvent.EventType.UP)
                        {
                            LeftTrackpad.Opacity -= 0.25;
                        }
                    }
                    break;

                case TouchTarget.TrackpadRight:
                    {
                        if (args.Status == CursorEvent.EventType.DOWN)
                        {
                            RightTrackpad.Opacity += 0.25;
                            var elapsed = time - prevRightTrackPadTime;
                            if (elapsed < 200)
                                args.Flags = 30; // double tap
                            prevRightTrackPadTime = time;
                        }
                        else if (args.Status == CursorEvent.EventType.UP)
                        {
                            RightTrackpad.Opacity -= 0.25;
                        }

                        normalizedX += 0.5d;
                    }
                    break;
            }

            this.pipeClient.SendMessage(new PipeClientCursor
            {
                action = args.Status == CursorEvent.EventType.DOWN ? CursorAction.CursorDown : args.Status == CursorEvent.EventType.UP ? CursorAction.CursorUp : CursorAction.CursorMove,
                x = normalizedX,
                y = normalizedY,
                button = Button,
                flags = args.Flags
            });
        }

        #region ModelVisual3D
        private RotateTransform3D DeviceRotateTransform;
        private RotateTransform3D DeviceRotateTransformFaceCameraX;
        private RotateTransform3D DeviceRotateTransformFaceCameraY;
        private RotateTransform3D DeviceRotateTransformFaceCameraZ;
        private RotateTransform3D LeftJoystickRotateTransform;
        private RotateTransform3D RightJoystickRotateTransform;
        private RotateTransform3D TransformTriggerPositionLeft;
        private RotateTransform3D TransformTriggerPositionRight;

        private int m_ModelVisualUpdate;
        private void OnServerMessage(object sender, PipeMessage message)
        {
            switch (message.code)
            {
                case PipeCode.SERVER_SENSOR:
                    PipeSensor sensor = (PipeSensor)message;

                    switch (sensor.type)
                    {
                        case SensorType.Quaternion:
                            UpdateModelVisual3D(sensor.q_w, sensor.q_x, sensor.q_y, sensor.q_z, sensor.x, sensor.y, sensor.z);
                            break;
                    }
                    break;
            }
        }

        internal void UpdateController(ControllerEx controllerEx)
        {
            this.controllerEx = controllerEx;
        }

        private bool controllerTriggered = false;
        private bool trackpadTriggered = false;

        public GamepadButtonFlags controllerTrigger = GamepadButtonFlags.DPadUp;
        public GamepadButtonFlags trackpadTrigger = GamepadButtonFlags.DPadDown;

        private void UpdateReport(object? sender, EventArgs e)
        {
            // get current gamepad state
            if (controllerEx != null && controllerEx.IsConnected())
            {
                GamepadState = controllerEx.GetState();
                Gamepad = GamepadState.Gamepad;
            }

            // Handle controller trigger(s)
            if (!ControllerTriggerListening && Gamepad.Buttons.HasFlag(controllerTrigger))
            {
                if (!controllerTriggered)
                {
                    UpdateControllerVisibility();
                    UpdateVisibility();
                    controllerTriggered = true;
                }
            }
            else if (controllerTriggered)
            {
                controllerTriggered = false;
            }

            // handle controller trigger(s) update
            if (ControllerTriggerListening)
            {
                if (Gamepad.Buttons != 0)
                    controllerTrigger |= Gamepad.Buttons;
                else if (Gamepad.Buttons == 0 && controllerTrigger != 0)
                {
                    ControllerTriggerUpdated?.Invoke(controllerTrigger);
                    ControllerTriggerListening = false;
                }
            }

            // Handle trackpad trigger(s)
            if (!TrackpadsTriggerListening && Gamepad.Buttons.HasFlag(trackpadTrigger))
            {
                if (!trackpadTriggered)
                {
                    UpdateTrackpadsVisibility();
                    UpdateVisibility();
                    trackpadTriggered = true;
                }
            }
            else if (trackpadTriggered)
            {
                trackpadTriggered = false;
            }

            // handle trackpad trigger(s) update
            if (TrackpadsTriggerListening)
            {
                if (Gamepad.Buttons != 0)
                    trackpadTrigger |= Gamepad.Buttons;
                else if (Gamepad.Buttons == 0 && trackpadTrigger != 0)
                {
                    TrackpadsTriggerUpdated?.Invoke(trackpadTrigger);
                    TrackpadsTriggerListening = false;
                }
            }

            if (VirtualController.Visibility != Visibility.Visible)
                return;

            this.Dispatcher.Invoke(() =>
            {
                GeometryModel3D model = null;
                foreach (GamepadButtonFlags button in Enum.GetValues(typeof(GamepadButtonFlags)))
                {
                    if (!CurrentModel.ButtonMap.ContainsKey(button))
                        continue;

                    foreach (Model3DGroup modelgroup in CurrentModel.ButtonMap[button])
                    {
                        model = (GeometryModel3D)modelgroup.Children.FirstOrDefault();
                        if (Gamepad.Buttons.HasFlag(button))
                            model.Material = CurrentModel.MaterialHighlight;
                        else
                            model.Material = CurrentModel.MaterialPlasticBlack;
                    }
                }

                // TODO update motor placeholders!
                // Motor Left
                model = CurrentModel.LeftMotor.Children[0] as GeometryModel3D;
                if (MotorLeftPlaceholder > 0)
                {
                    model.Material = CurrentModel.MaterialHighlight;
                }
                else
                {
                    model.Material = CurrentModel.MaterialPlasticWhite;
                }

                // Motor Right
                model = CurrentModel.RightMotor.Children[0] as GeometryModel3D;
                if (MotorRightPlaceholder > 0)
                {
                    model.Material = CurrentModel.MaterialHighlight;
                }
                else
                {
                    model.Material = CurrentModel.MaterialPlasticWhite;
                }

                // ShoulderLeftTrigger
                model = CurrentModel.LeftShoulderTrigger.Children[0] as GeometryModel3D;
                TriggerAngleShoulderLeft = -1 * CurrentModel.TriggerMaxAngleDeg * (float)Gamepad.LeftTrigger / (float)byte.MaxValue;

                if (Gamepad.LeftTrigger > 0)
                {
                    model.Material = CurrentModel.MaterialHighlight;

                }
                else
                {
                    model.Material = CurrentModel.MaterialPlasticBlack;
                }

                // ShoulderRightTrigger
                model = CurrentModel.RightShoulderTrigger.Children[0] as GeometryModel3D;
                TriggerAngleShoulderRight = -1 * CurrentModel.TriggerMaxAngleDeg * (float)Gamepad.RightTrigger / (float)byte.MaxValue;

                if (Gamepad.RightTrigger > 0)
                {
                    model.Material = CurrentModel.MaterialHighlight;
                }
                else
                {
                    model.Material = CurrentModel.MaterialPlasticBlack;
                }

                // JoystickLeftRing
                model = CurrentModel.LeftThumbRing.Children[0] as GeometryModel3D;
                if (Gamepad.LeftThumbX != 0 || Gamepad.LeftThumbY != 0)
                {
                    // Adjust color
                    model.Material = CurrentModel.MaterialHighlight;

                    // Define and compute
                    Transform3DGroup Transform3DGroupJoystickLeft = new Transform3DGroup();
                    float x = CurrentModel.JoystickMaxAngleDeg * (float)Gamepad.LeftThumbX / (float)short.MaxValue;
                    float y = -1 * CurrentModel.JoystickMaxAngleDeg * (float)Gamepad.LeftThumbY / (float)short.MaxValue;

                    // Rotation X
                    var ax3d = new AxisAngleRotation3D(new Vector3D(0, 0, 1), x);
                    LeftJoystickRotateTransform = new RotateTransform3D(ax3d);

                    // Define rotation point
                    LeftJoystickRotateTransform.CenterX = CurrentModel.JoystickRotationPointCenterLeftMillimeter.X;
                    LeftJoystickRotateTransform.CenterY = CurrentModel.JoystickRotationPointCenterLeftMillimeter.Y;
                    LeftJoystickRotateTransform.CenterZ = CurrentModel.JoystickRotationPointCenterLeftMillimeter.Z;

                    Transform3DGroupJoystickLeft.Children.Add(LeftJoystickRotateTransform);

                    // Rotation Y
                    ax3d = new AxisAngleRotation3D(new Vector3D(1, 0, 0), y);
                    LeftJoystickRotateTransform = new RotateTransform3D(ax3d);

                    // Define rotation point
                    LeftJoystickRotateTransform.CenterX = CurrentModel.JoystickRotationPointCenterLeftMillimeter.X;
                    LeftJoystickRotateTransform.CenterY = CurrentModel.JoystickRotationPointCenterLeftMillimeter.Y;
                    LeftJoystickRotateTransform.CenterZ = CurrentModel.JoystickRotationPointCenterLeftMillimeter.Z;

                    Transform3DGroupJoystickLeft.Children.Add(LeftJoystickRotateTransform);

                    // Transform joystick group
                    CurrentModel.LeftThumbRing.Transform = CurrentModel.LeftThumb.Transform = Transform3DGroupJoystickLeft;
                }
                else
                {
                    // Default material color, no highlight
                    model.Material = CurrentModel.MaterialPlasticBlack;

                    // Define and compute, back to default position
                    var ax3d = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
                    LeftJoystickRotateTransform = new RotateTransform3D(ax3d);

                    // Define rotation point
                    LeftJoystickRotateTransform.CenterX = CurrentModel.JoystickRotationPointCenterLeftMillimeter.X;
                    LeftJoystickRotateTransform.CenterY = CurrentModel.JoystickRotationPointCenterLeftMillimeter.Y;
                    LeftJoystickRotateTransform.CenterZ = CurrentModel.JoystickRotationPointCenterLeftMillimeter.Z;

                    // Transform joystick
                    CurrentModel.LeftThumbRing.Transform = CurrentModel.LeftThumb.Transform = LeftJoystickRotateTransform;
                }

                // JoystickRightRing
                model = CurrentModel.RightThumbRing.Children[0] as GeometryModel3D;
                if (Gamepad.RightThumbX != 0 || Gamepad.RightThumbY != 0)
                {
                    model.Material = CurrentModel.MaterialHighlight;

                    // Define and compute
                    Transform3DGroup Transform3DGroupJoystickRight = new Transform3DGroup();
                    float x = CurrentModel.JoystickMaxAngleDeg * (float)Gamepad.RightThumbX / (float)short.MaxValue;
                    float y = -1 * CurrentModel.JoystickMaxAngleDeg * (float)Gamepad.RightThumbY / (float)short.MaxValue;

                    // Rotation X
                    var ax3d = new AxisAngleRotation3D(new Vector3D(0, 0, 1), x);
                    RightJoystickRotateTransform = new RotateTransform3D(ax3d);

                    // Define rotation point
                    RightJoystickRotateTransform.CenterX = CurrentModel.JoystickRotationPointCenterRightMillimeter.X;
                    RightJoystickRotateTransform.CenterY = CurrentModel.JoystickRotationPointCenterRightMillimeter.Y;
                    RightJoystickRotateTransform.CenterZ = CurrentModel.JoystickRotationPointCenterRightMillimeter.Z;

                    Transform3DGroupJoystickRight.Children.Add(RightJoystickRotateTransform);

                    // Rotation Y
                    ax3d = new AxisAngleRotation3D(new Vector3D(1, 0, 0), y);
                    RightJoystickRotateTransform = new RotateTransform3D(ax3d);

                    // Define rotation point
                    RightJoystickRotateTransform.CenterX = CurrentModel.JoystickRotationPointCenterRightMillimeter.X;
                    RightJoystickRotateTransform.CenterY = CurrentModel.JoystickRotationPointCenterRightMillimeter.Y;
                    RightJoystickRotateTransform.CenterZ = CurrentModel.JoystickRotationPointCenterRightMillimeter.Z;

                    Transform3DGroupJoystickRight.Children.Add(RightJoystickRotateTransform);

                    // Transform joystick group
                    CurrentModel.RightThumbRing.Transform = CurrentModel.RightThumb.Transform = Transform3DGroupJoystickRight;

                }
                else
                {
                    model.Material = CurrentModel.MaterialPlasticBlack;

                    // Define and compute, back to default position
                    var ax3d = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
                    RightJoystickRotateTransform = new RotateTransform3D(ax3d);

                    // Define rotation point
                    RightJoystickRotateTransform.CenterX = CurrentModel.JoystickRotationPointCenterRightMillimeter.X;
                    RightJoystickRotateTransform.CenterY = CurrentModel.JoystickRotationPointCenterRightMillimeter.Y;
                    RightJoystickRotateTransform.CenterZ = CurrentModel.JoystickRotationPointCenterRightMillimeter.Z;

                    // Transform joystick
                    CurrentModel.RightThumbRing.Transform = CurrentModel.RightThumb.Transform = RightJoystickRotateTransform;
                }
            });
        }

        public void ControllerTriggerClicked()
        {
            controllerTrigger = 0;
            ControllerTriggerListening = true;
        }

        public void TrackpadsTriggerClicked()
        {
            trackpadTrigger = 0;
            TrackpadsTriggerListening = true;
        }

        public void UpdateControllerVisibility()
        {
            this.Dispatcher.Invoke(() =>
            {
                Visibility visibility = Visibility.Visible;
                switch (VirtualController.Visibility)
                {
                    case Visibility.Visible:
                        visibility = Visibility.Hidden;
                        break;
                    case Visibility.Hidden:
                        visibility = Visibility.Visible;
                        break;
                }
                VirtualController.Visibility = visibility;
            });
        }

        public void UpdateTrackpadsVisibility()
        {
            this.Dispatcher.Invoke(() =>
            {
                Visibility visibility = Visibility.Visible;
                switch (VirtualTrackpads.Visibility)
                {
                    case Visibility.Visible:
                        visibility = Visibility.Hidden;
                        break;
                    case Visibility.Hidden:
                        visibility = Visibility.Visible;
                        break;
                }
                VirtualTrackpads.Visibility = visibility;
            });
        }

        private void UpdateVisibility()
        {
            this.Dispatcher.Invoke(() =>
            {
                this.Visibility = (VirtualController.Visibility == Visibility.Visible || VirtualTrackpads.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Hidden;
            });
            pipeClient.SendMessage(new PipeOverlay((int)VirtualController.Visibility));
        }

        private void UpwardVisibilityRotationShoulderButtons(float ShoulderButtonsAngleDeg, 
                                                             Vector3D UpwardVisibilityRotationAxis, 
                                                             Vector3D UpwardVisibilityRotationPoint,
                                                             float ShoulderTriggerAngleDeg,
                                                             Vector3D ShoulderTriggerRotationPointCenterMillimeter,
                                                             ref Model3DGroup ShoulderTrigger,
                                                             ref Model3DGroup ShoulderButton
                                                            )
        {
            // Define rotation group for trigger button to combine rotations
            Transform3DGroup Transform3DGroupShoulderTrigger = new Transform3DGroup();

            // Upward visibility rotation vector and angle
            var ax3d = new AxisAngleRotation3D(UpwardVisibilityRotationAxis, ShoulderButtonsAngleDeg);
            RotateTransform3D TransformShoulder = new RotateTransform3D(ax3d);

            // Define rotation point shoulder buttons
            TransformShoulder.CenterX = UpwardVisibilityRotationPoint.X;
            TransformShoulder.CenterY = UpwardVisibilityRotationPoint.Y;
            TransformShoulder.CenterZ = UpwardVisibilityRotationPoint.Z;

            // Trigger vector and angle
            ax3d = new AxisAngleRotation3D(UpwardVisibilityRotationAxis, ShoulderTriggerAngleDeg);
            RotateTransform3D TransformTriggerPosition = new RotateTransform3D(ax3d);

            // Define rotation point trigger
            TransformTriggerPosition.CenterX = ShoulderTriggerRotationPointCenterMillimeter.X;
            TransformTriggerPosition.CenterY = ShoulderTriggerRotationPointCenterMillimeter.Y;
            TransformTriggerPosition.CenterZ = ShoulderTriggerRotationPointCenterMillimeter.Z;

            // Transform trigger
            // Trigger first, then visibility transform
            Transform3DGroupShoulderTrigger.Children.Add(TransformTriggerPosition);
            Transform3DGroupShoulderTrigger.Children.Add(TransformShoulder);

            // Transform trigger with both upward visibility and trigger position
            ShoulderTrigger.Transform = Transform3DGroupShoulderTrigger;
            // Transform shoulder button only with upward visibility
            ShoulderButton.Transform = TransformShoulder;
        }

        public Vector3D DesiredAngle = new Vector3D(0, 0, 0);
        private void UpdateModelVisual3D(float q_w, float q_x, float q_y, float q_z, float x, float y, float z)
        {
            m_ModelVisualUpdate++;

            /* reduce CPU usage by drawing every x calls
            if (m_ModelVisualUpdate % 2 != 0)
                return; */

            this.Dispatcher.Invoke(() =>
            {
                Transform3DGroup Transform3DGroupModel = new Transform3DGroup();

                // Device transformation based on pose
                Quaternion DevicePose = new Quaternion(q_w, q_x, q_y, q_z);
                var Ax3DDevicePose = new QuaternionRotation3D(DevicePose);
                DeviceRotateTransform = new RotateTransform3D(Ax3DDevicePose);
                Transform3DGroupModel.Children.Add(DeviceRotateTransform);

                // Angles
                Vector3D DiffAngle = new Vector3D(0, 0, 0);

                // Determine diff angles
                DiffAngle.X = (InputUtils.rad2deg(x) - (float)FaceCameraObjectAlignment.X) - (float)DesiredAngle.X;
                DiffAngle.Y = (InputUtils.rad2deg(y) - (float)FaceCameraObjectAlignment.Y) - (float)DesiredAngle.Y;
                DiffAngle.Z = (InputUtils.rad2deg(z) - (float)FaceCameraObjectAlignment.Z) - (float)DesiredAngle.Z;

                // Handle wrap around at -180 +180 position which is horizontal for steering
                DiffAngle.Y = (y < 0.0) ? DiffAngle.Y += 180.0f : DiffAngle.Y -= 180.0f;

                // Correction amount for camera, increase slowly
                FaceCameraObjectAlignment += DiffAngle * 0.0015; // 0.0015 = ~90 degrees in 30 seconds

                // Transform YZX
                var Ax3DFaceCameraY = new AxisAngleRotation3D(new Vector3D(0, 1, 0), FaceCameraObjectAlignment.Y);
                DeviceRotateTransformFaceCameraY = new RotateTransform3D(Ax3DFaceCameraY);
                Transform3DGroupModel.Children.Add(DeviceRotateTransformFaceCameraY);

                var Ax3DFaceCameraZ = new AxisAngleRotation3D(new Vector3D(0, 0, 1), -FaceCameraObjectAlignment.Z);
                DeviceRotateTransformFaceCameraZ = new RotateTransform3D(Ax3DFaceCameraZ);
                Transform3DGroupModel.Children.Add(DeviceRotateTransformFaceCameraZ);

                var Ax3DFaceCameraX = new AxisAngleRotation3D(new Vector3D(1, 0, 0), FaceCameraObjectAlignment.X);
                DeviceRotateTransformFaceCameraX = new RotateTransform3D(Ax3DFaceCameraX);
                Transform3DGroupModel.Children.Add(DeviceRotateTransformFaceCameraX);

                // Transform mode with group
                ModelVisual3D.Content.Transform = Transform3DGroupModel;

                // Upward visibility rotation for shoulder buttons
                // Model angle to compensate for
                float ModelPoseXDeg = InputUtils.rad2deg(x) - (float)FaceCameraObjectAlignment.X;
                float ShoulderButtonsAngleDeg = 0.0f;

                // Rotate shoulder 90 degrees upward while controller faces user
                if (ModelPoseXDeg < 0)
                {
                    ShoulderButtonsAngleDeg = Math.Clamp(90.0f - (1 * ModelPoseXDeg), 90.0f, 180.0f);
                }
                // In between rotate inverted from pose
                else if (ModelPoseXDeg >= 0 && ModelPoseXDeg <= 45.0f)
                {
                    ShoulderButtonsAngleDeg = 90.0f - (2 * ModelPoseXDeg);
                }
                // Rotate shoulder buttons to original spot at -45 and beyond
                else if (ModelPoseXDeg < 45.0f)
                {
                    ShoulderButtonsAngleDeg = 0.0f;
                }

                // Left shoulder buttons visibility rotation and trigger button angle
                Model3DGroup Placeholder = CurrentModel.ButtonMap[GamepadButtonFlags.LeftShoulder][0];

                UpwardVisibilityRotationShoulderButtons(ShoulderButtonsAngleDeg,
                                                        CurrentModel.UpwardVisibilityRotationAxisLeft,
                                                        CurrentModel.UpwardVisibilityRotationPointLeft,
                                                        TriggerAngleShoulderLeft,
                                                        CurrentModel.ShoulderTriggerRotationPointCenterLeftMillimeter,
                                                        ref CurrentModel.LeftShoulderTrigger,
                                                        ref Placeholder
                                                        );

                CurrentModel.ButtonMap[GamepadButtonFlags.LeftShoulder][0] = Placeholder;

                // Right shoulder buttons visibility rotation and trigger button angle
                Placeholder = CurrentModel.ButtonMap[GamepadButtonFlags.RightShoulder][0];

                UpwardVisibilityRotationShoulderButtons(ShoulderButtonsAngleDeg,
                                                        CurrentModel.UpwardVisibilityRotationAxisRight,
                                                        CurrentModel.UpwardVisibilityRotationPointRight,
                                                        TriggerAngleShoulderRight,
                                                        CurrentModel.ShoulderTriggerRotationPointCenterRightMillimeter,
                                                        ref CurrentModel.RightShoulderTrigger,
                                                        ref Placeholder
                                                        );

                CurrentModel.ButtonMap[GamepadButtonFlags.RightShoulder][0] = Placeholder;

            });
        }
        #endregion

        #region Hook
        protected void WinEventCallback(
            IntPtr hWinEventHook,
            NativeMethods.SWEH_Events eventType,
            IntPtr hWnd,
            NativeMethods.SWEH_ObjectId idObject,
            long idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (hWnd == this.hWnd &&
                eventType == NativeMethods.SWEH_Events.EVENT_OBJECT_LOCATIONCHANGE &&
                idObject == (NativeMethods.SWEH_ObjectId)NativeMethods.SWEH_CHILDID_SELF)
                {
                    var rect = GetWindowRectangle(hWnd);
                    this.Top = rect.Top;
                    this.Left = rect.Left;
                    this.Width = rect.Right - rect.Left;
                    this.Height = rect.Bottom - rect.Top;

                    this.OverlayPosition = new Point(rect.Left, rect.Top);
                    this.LeftTrackPadPosition = LeftTrackpad.PointToScreen(new Point(0, 0));
                    this.RightTrackPadPosition = RightTrackpad.PointToScreen(new Point(0, 0));
                }
            }
            catch (Exception ex) { }
        }

        public void HookInto(uint processid)
        {
            try
            {
                targetProc = Process.GetProcessById((int)processid);

                if (targetProc != null)
                {
                    hWnd = targetProc.MainWindowHandle;

                    if (hWnd != IntPtr.Zero)
                    {
                        uint targetThreadId = GetWindowThread(hWnd);
                        isHooked = true;

                        hWinEventHook = WinEventHookOne(
                            NativeMethods.SWEH_Events.EVENT_OBJECT_LOCATIONCHANGE,
                            WinEventDelegate, (uint)targetProc.Id, targetThreadId);

                        var rect = GetWindowRectangle(hWnd);

                        this.Top = rect.Top;
                        this.Left = rect.Left;
                        this.Width = rect.Right - rect.Left;
                        this.Height = rect.Bottom - rect.Top;

                        this.OverlayPosition = new Point(rect.Left, rect.Top);
                    }
                }
            }
            catch (Exception ex) { UnHook(); }
        }

        public void UnHook()
        {
            UpdateControllerVisibility();

            targetProc = null;
            hWnd = IntPtr.Zero;
            isHooked = false;
        }
        #endregion
    }
}