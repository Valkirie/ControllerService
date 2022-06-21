using ControllerCommon;
using ControllerCommon.Devices;
using ControllerCommon.Utils;
using HandheldCompanion.Managers;
using HandheldCompanion.Models;
using SharpDX.XInput;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using TouchEventSample;
using static TouchEventSample.TouchSourceWinTouch;
using GamepadButtonFlags = SharpDX.XInput.GamepadButtonFlags;

namespace HandheldCompanion.Views.Windows
{
    /// <summary>
    /// Interaction logic for Overlay.xaml
    /// </summary>
    public partial class Overlay : Window
    {
        #region import
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        #endregion

        private PipeClient pipeClient;
        private MultimediaTimer UpdateTimer;

        private InputsManager inputsManager;
        private Model CurrentModel;
        private OverlayModelMode Modelmode;
        private HIDmode HIDmode;

        private GeometryModel3D model;
        private Point OverlayPosition;

        private enum TouchTarget
        {
            TrackpadLeft = 0,
            TrackpadRight = 1
        }

        private TouchSourceWinTouch touchsource;
        private long prevLeftTrackPadTime;
        private long prevRightTrackPadTime;
        private TouchTarget target;

        private Vector3D FaceCameraObjectAlignment = new Vector3D(0.0d, 0.0d, 0.0d);

        public Vector3D DesiredAngleDeg = new Vector3D(0, 0, 0);
        private float q_w = 0.0f, q_x = 0.0f, q_y = 1.0f, q_z = 0.0f;
        private Vector3D PoseRad = new Vector3D(0, 3.14, 0);

        // TODO Dummy variables, placeholder and for testing 
        private short MotorLeftPlaceholder;
        private short MotorRightPlaceholder;

        private float TriggerAngleShoulderLeft;
        private float TriggerAngleShoulderRight;

        public Overlay()
        {
            InitializeComponent();
            this.SourceInitialized += Overlay_SourceInitialized;
        }

        public Overlay(PipeClient pipeClient, InputsManager inputsManager) : this()
        {
            this.pipeClient = pipeClient;
            this.pipeClient.ServerMessage += OnServerMessage;

            this.inputsManager = inputsManager;
            this.inputsManager.Updated += UpdateReport;

            // touch vars
            touchsource = new TouchSourceWinTouch(this);
            touchsource.Touch += Touchsource_Touch;

            // initialize timers
            UpdateTimer = new MultimediaTimer();
            UpdateTimer.Tick += DrawModel;

            UpdateModel();
        }

        private void Overlay_SourceInitialized(object? sender, EventArgs e)
        {
            //Set the window style to noactivate.
            WindowInteropHelper helper = new WindowInteropHelper(this);
            SetWindowLong(helper.Handle, GWL_EXSTYLE,
                GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdateTimer.Tick -= DrawModel;
            UpdateTimer.Stop();
        }

        public void UpdateInterval(double interval)
        {
            UpdateTimer.Stop();
            UpdateTimer.Interval = (int)interval;
            UpdateTimer.Start();
        }

        public void UpdateHIDMode(HIDmode HIDmode)
        {
            if (this.HIDmode == HIDmode)
                return;

            this.HIDmode = HIDmode;
            UpdateModel();
        }

        public void UpdateOverlayMode(OverlayModelMode Modelmode)
        {
            if (this.Modelmode == Modelmode)
                return;

            this.Modelmode = Modelmode;
            UpdateModel();
        }

        public void UpdateModel()
        {
            Model newModel = null;
            switch (Modelmode)
            {
                default:
                case OverlayModelMode.OEM:
                    {
                        switch (MainWindow.handheldDevice.ProductName)
                        {
                            case "AYANEO 2021":
                            case "AYANEO 2021 Pro":
                            case "AYANEO 2021 Pro Retro Power":
                                newModel = new ModelAYANEO2021();
                                break;
                            case "NEXT Pro":
                            case "NEXT Advance":
                            case "NEXT":
                                newModel = new ModelAYANEONext();
                                break;
                            default:
                                // default model if unsupported OEM
                                newModel = new ModelXBOX360();
                                break;
                        }
                    }
                    break;
                case OverlayModelMode.Virtual:
                    {
                        switch(HIDmode)
                        {
                            default:
                            case HIDmode.DualShock4Controller:
                                newModel = new ModelDS4();
                                break;
                            case HIDmode.Xbox360Controller:
                                newModel = new ModelXBOX360();
                                break;
                        }
                    }
                    break;
                case OverlayModelMode.ZDOPlus:
                    {
                        newModel = new ModelZDOPlus();
                    }
                    break;
                case OverlayModelMode.Toy:
                    {
                        newModel = new ModelToyController();
                    }
                    break;
            }

            if (newModel != CurrentModel)
            {
                CurrentModel = newModel;

                ModelVisual3D.Content = CurrentModel.model3DGroup;
                ModelViewPort.ZoomExtents();
            }
        }

        public double GetWindowsScaling()
        {
            return SystemParameters.PrimaryScreenWidth / Screen.PrimaryScreen.Bounds.Width;
        }

        private void Touchsource_Touch(TouchArgs args, long time)
        {
            double X = args.LocationX - this.OverlayPosition.X;
            // double Y = args.LocationY - this.OverlayPosition.Y;

            double CenterX = Screen.PrimaryScreen.Bounds.Width / 2;
            target = X < CenterX ? TouchTarget.TrackpadLeft : TouchTarget.TrackpadRight;

            CursorButton Button = CursorButton.None;
            Point CurrentPoint;

            switch (target)
            {
                default:
                case TouchTarget.TrackpadLeft:
                    Button = CursorButton.TouchLeft;
                    CurrentPoint = LeftTrackpad.PointToScreen(new Point(0, 0));
                    break;
                case TouchTarget.TrackpadRight:
                    Button = CursorButton.TouchRight;
                    CurrentPoint = RightTrackpad.PointToScreen(new Point(0, 0));
                    break;
            }

            var dpi = GetWindowsScaling(); // todo: use me !

            var relativeX = Math.Clamp(args.LocationX - CurrentPoint.X, 0, LeftTrackpad.ActualWidth);
            var relativeY = Math.Clamp(args.LocationY - CurrentPoint.Y, 0, LeftTrackpad.ActualHeight);

            var normalizedX = (relativeX / LeftTrackpad.ActualWidth) / 2.0d;
            var normalizedY = relativeY / LeftTrackpad.ActualHeight;

            Debug.WriteLine("touchX:{0}, touchY:{1}", args.LocationX, args.LocationY);
            Debug.WriteLine("CurrentPointX:{0}, CurrentPointY:{1}", CurrentPoint.X, CurrentPoint.Y);
            Debug.WriteLine("relativeX:{0}, relativeY:{1}", relativeX, relativeY);
            Debug.WriteLine("normalizedX:{0}, normalizedY:{1}", normalizedX, normalizedY);

            switch (target)
            {
                default:
                case TouchTarget.TrackpadLeft:
                    {
                        if (args.Status == CursorEvent.EventType.DOWN)
                        {
                            LeftTrackpad.Opacity += 0.10;
                            var elapsed = time - prevLeftTrackPadTime;
                            if (elapsed < 200)
                                args.Flags = 30; // double tap
                            prevLeftTrackPadTime = time;
                        }
                        else if (args.Status == CursorEvent.EventType.UP)
                            LeftTrackpad.Opacity -= 0.10;
                    }
                    break;

                case TouchTarget.TrackpadRight:
                    {
                        if (args.Status == CursorEvent.EventType.DOWN)
                        {
                            RightTrackpad.Opacity += 0.10;
                            var elapsed = time - prevRightTrackPadTime;
                            if (elapsed < 200)
                                args.Flags = 30; // double tap
                            prevRightTrackPadTime = time;
                        }
                        else if (args.Status == CursorEvent.EventType.UP)
                            RightTrackpad.Opacity -= 0.10;

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

        public void UpdateControllerVisibility()
        {
            this.Dispatcher.Invoke(() =>
            {
                Visibility visibility = Visibility.Visible;
                switch (VirtualController.Visibility)
                {
                    case Visibility.Visible:
                        visibility = Visibility.Collapsed;

                        // reset position
                        q_w = q_x = q_z = 0.0f;
                        q_y = 1.0f;
                        PoseRad = new Vector3D(0.0d, 3.14d, 0.0d);

                        break;
                    case Visibility.Collapsed:
                    case Visibility.Hidden:
                        visibility = Visibility.Visible;
                        break;
                }
                VirtualController.Visibility = visibility;
                UpdateVisibility();
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
                        visibility = Visibility.Collapsed;
                        break;
                    case Visibility.Collapsed:
                    case Visibility.Hidden:                        
                        visibility = Visibility.Visible;
                        break;
                }
                VirtualTrackpads.Visibility = visibility;
                UpdateVisibility();
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

        #region ModelVisual3D
        private RotateTransform3D DeviceRotateTransform;
        private RotateTransform3D DeviceRotateTransformFaceCameraX;
        private RotateTransform3D DeviceRotateTransformFaceCameraY;
        private RotateTransform3D DeviceRotateTransformFaceCameraZ;
        private RotateTransform3D LeftJoystickRotateTransform;
        private RotateTransform3D RightJoystickRotateTransform;
        private RotateTransform3D TransformTriggerPositionLeft;
        private RotateTransform3D TransformTriggerPositionRight;

        private void OnServerMessage(object sender, PipeMessage message)
        {
            switch (message.code)
            {
                case PipeCode.SERVER_SENSOR:
                    {
                        // prevent late PipeMessage to apply
                        if (this.Visibility != Visibility.Visible)
                            return;

                        PipeSensor sensor = (PipeSensor)message;
                        switch (sensor.type)
                        {
                            case SensorType.Quaternion:
                                q_w = sensor.q_w;
                                q_x = sensor.q_x;
                                q_y = sensor.q_y;
                                q_z = sensor.q_z;

                                PoseRad.X = sensor.x;
                                PoseRad.Y = sensor.y;
                                PoseRad.Z = sensor.z;
                                break;
                        }
                    }
                    break;
            }
        }

        private void DrawModel(object? sender, EventArgs e)
        {
            if (CurrentModel is null)
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

                        if (model.Material.GetType() != typeof(DiffuseMaterial))
                            continue;

                        model.Material = gamepad.Buttons.HasFlag(button) ? CurrentModel.HighlightMaterials[modelgroup] : CurrentModel.DefaultMaterials[modelgroup];
                    }
                }
            });

            // skip virtual controller update if hidden or collapsed
            if (VirtualController.Visibility != Visibility.Visible)
                return;

            // update model
            UpdateModelVisual3D();

            this.Dispatcher.Invoke(() =>
            {
                float GradientFactor; // Used for multiple models

                // TODO update motor placeholders!
                // Motor Left
                model = CurrentModel.LeftMotor.Children[0] as GeometryModel3D;
                model.Material = MotorLeftPlaceholder > 0 ? CurrentModel.HighlightMaterials[CurrentModel.LeftMotor] : CurrentModel.DefaultMaterials[CurrentModel.LeftMotor];

                // Motor Right
                model = CurrentModel.RightMotor.Children[0] as GeometryModel3D;
                model.Material = MotorRightPlaceholder > 0 ? CurrentModel.HighlightMaterials[CurrentModel.RightMotor] : CurrentModel.DefaultMaterials[CurrentModel.RightMotor];

                // ShoulderLeftTrigger
                model = CurrentModel.LeftShoulderTrigger.Children[0] as GeometryModel3D;
                if (gamepad.LeftTrigger > 0)
                {
                    GradientFactor = 1 * (float)gamepad.LeftTrigger / (float)byte.MaxValue;
                    model.Material = GradientHighlight(CurrentModel.DefaultMaterials[CurrentModel.LeftShoulderTrigger],
                                                       CurrentModel.HighlightMaterials[CurrentModel.LeftShoulderTrigger],
                                                       GradientFactor);
                }
                else
                {
                    model.Material = CurrentModel.DefaultMaterials[CurrentModel.LeftShoulderTrigger];
                }

                TriggerAngleShoulderLeft = -1 * CurrentModel.TriggerMaxAngleDeg * (float)gamepad.LeftTrigger / (float)byte.MaxValue;

                // ShoulderRightTrigger
                model = CurrentModel.RightShoulderTrigger.Children[0] as GeometryModel3D;

                if (gamepad.RightTrigger > 0)
                {
                    GradientFactor = 1 * (float)gamepad.RightTrigger / (float)byte.MaxValue;
                    model.Material = GradientHighlight(CurrentModel.DefaultMaterials[CurrentModel.RightShoulderTrigger],
                                                       CurrentModel.HighlightMaterials[CurrentModel.RightShoulderTrigger],
                                                       GradientFactor);
                }
                else
                {
                    model.Material = CurrentModel.DefaultMaterials[CurrentModel.RightShoulderTrigger];
                }

                TriggerAngleShoulderRight = -1 * CurrentModel.TriggerMaxAngleDeg * (float)gamepad.RightTrigger / (float)byte.MaxValue;

                // JoystickLeftRing
                model = CurrentModel.LeftThumbRing.Children[0] as GeometryModel3D;
                if (gamepad.LeftThumbX != 0 || gamepad.LeftThumbY != 0)
                {
                    // Adjust color
                    GradientFactor = Math.Max(Math.Abs(1 * (float)gamepad.LeftThumbX / (float)short.MaxValue),
                                              Math.Abs(1 * (float)gamepad.LeftThumbY / (float)short.MaxValue));

                    model.Material = GradientHighlight(CurrentModel.DefaultMaterials[CurrentModel.LeftThumbRing],
                                                       CurrentModel.HighlightMaterials[CurrentModel.LeftThumbRing],
                                                       GradientFactor);

                    // Define and compute
                    Transform3DGroup Transform3DGroupJoystickLeft = new Transform3DGroup();
                    float x = CurrentModel.JoystickMaxAngleDeg * (float)gamepad.LeftThumbX / (float)short.MaxValue;
                    float y = -1 * CurrentModel.JoystickMaxAngleDeg * (float)gamepad.LeftThumbY / (float)short.MaxValue;

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
                    model.Material = CurrentModel.DefaultMaterials[CurrentModel.LeftThumbRing];

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
                if (gamepad.RightThumbX != 0 || gamepad.RightThumbY != 0)
                {
                    // Adjust color
                    GradientFactor = Math.Max(Math.Abs(1 * (float)gamepad.RightThumbX / (float)short.MaxValue),
                                              Math.Abs(1 * (float)gamepad.RightThumbY / (float)short.MaxValue));

                    model.Material = GradientHighlight(CurrentModel.DefaultMaterials[CurrentModel.RightThumbRing],
                                                       CurrentModel.HighlightMaterials[CurrentModel.RightThumbRing],
                                                       GradientFactor);

                    // Define and compute
                    Transform3DGroup Transform3DGroupJoystickRight = new Transform3DGroup();
                    float x = CurrentModel.JoystickMaxAngleDeg * (float)gamepad.RightThumbX / (float)short.MaxValue;
                    float y = -1 * CurrentModel.JoystickMaxAngleDeg * (float)gamepad.RightThumbY / (float)short.MaxValue;

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
                    model.Material = CurrentModel.DefaultMaterials[CurrentModel.RightThumbRing];

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

        private Gamepad gamepad;
        private void UpdateReport(Gamepad gamepad)
        {
            this.gamepad = gamepad;
        }

        private Material GradientHighlight(Material DefaultMaterial, Material HighlightMaterial, float Factor)
        {
            // Determine colors from brush from materials
            Brush DefaultMaterialBrush = ((DiffuseMaterial)DefaultMaterial).Brush;
            Color StartColor = ((SolidColorBrush)DefaultMaterialBrush).Color;
            Brush HighlightMaterialBrush = ((DiffuseMaterial)HighlightMaterial).Brush;
            Color EndColor = ((SolidColorBrush)HighlightMaterialBrush).Color;

            // Linear interpolate color
            float bk = (1 - Factor);
            float a = StartColor.A * bk + EndColor.A * Factor;
            float r = StartColor.R * bk + EndColor.R * Factor;
            float g = StartColor.G * bk + EndColor.G * Factor;
            float b = StartColor.B * bk + EndColor.B * Factor;

            // Define color
            Color TransitionColor = Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);

            // Return material with transition color
            return new DiffuseMaterial(new SolidColorBrush(TransitionColor));
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

        private void UpdateModelVisual3D()
        {
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
                DiffAngle.X = (InputUtils.rad2deg((float)PoseRad.X) - (float)FaceCameraObjectAlignment.X) - (float)DesiredAngleDeg.X;
                DiffAngle.Y = (InputUtils.rad2deg((float)PoseRad.Y) - (float)FaceCameraObjectAlignment.Y) - (float)DesiredAngleDeg.Y;
                DiffAngle.Z = (InputUtils.rad2deg((float)PoseRad.Z) - (float)FaceCameraObjectAlignment.Z) - (float)DesiredAngleDeg.Z;

                // Handle wrap around at -180 +180 position which is horizontal for steering
                DiffAngle.Y = ((float)PoseRad.Y < 0.0) ? DiffAngle.Y += 180.0f : DiffAngle.Y -= 180.0f;

                // Correction amount for camera, increase slowly
                FaceCameraObjectAlignment += DiffAngle * 0.0005; // 0.0015 = ~90 degrees in 30 seconds

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
                float ModelPoseXDeg = InputUtils.rad2deg((float)PoseRad.X) - (float)FaceCameraObjectAlignment.X;
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
    }
}
