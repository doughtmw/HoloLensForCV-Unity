using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

#if ENABLE_WINMD_SUPPORT
using Windows.UI.Xaml;
using Windows.Graphics.Imaging;

// Include winrt components
using HoloLensForCV;
#endif

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.WSA.Input;
using System.Threading;


// App permissions, modify the appx file for research mode streams
// https://docs.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations

// Reimplement as list loop structure... 
namespace HoloLensForCVUnity
{
    // Using the hololens for cv .winmd file for runtime support
    // Build HoloLensForCV c++ project (x86) and copy all output files
    // to Assets->Plugins->x86
    // https://docs.unity3d.com/2018.4/Documentation/Manual/IL2CPP-WindowsRuntimeSupport.html
    public class SensorStreams : MonoBehaviour
    {
        public Text myText;

        public enum SensorTypeUnity
        {
            Undefined = -1,
            PhotoVideo = 0,
            ShortThrowToFDepth = 1,
            ShortThrowToFReflectivity = 2,
            LongThrowToFDepth = 3,
            LongThrowToFReflectivity = 4,
            VisibleLightLeftLeft = 5,
            VisibleLightLeftFront = 6,
            VisibleLightRightFront = 7,
            VisibleLightRightRight = 8,
            NumberOfSensorTypes = 9
        }
        public SensorTypeUnity sensorTypePv;
        public SensorTypeUnity sensorTypeShortDepth;

        /// <summary>
        /// GameObject to show video streams.
        /// </summary>
        public GameObject pvDepthGo;

        /// <summary>
        /// Public params for depth pv mapping.
        /// Measures in mm from camera.
        /// </summary>
        public int depthRangeFrom;
        public int depthRangeTo;
        public int patchRadius;

        /// <summary>
        /// Cached materials for applying to game objects.
        /// </summary>
        private Material _pvDepthMaterial;

        /// </summary>
        /// Textures created from input byte arrays.
        /// </summary>
        // PV
        private Texture2D _pvDepthTexture;

        private bool _mediaFrameSourceGroupsStarted = false;

#if ENABLE_WINMD_SUPPORT
        // Enable winmd support to include winmd files. Will not
        // run in Unity editor.
        private SensorFrameStreamer _sensorFrameStreamerPv;
        private SensorFrameStreamer _sensorFrameStreamerResearch;
        private SpatialPerception _spatialPerception;

        /// <summary>
        /// Media frame source groups for each sensor stream.
        /// </summary>
        private MediaFrameSourceGroup _pvMediaFrameSourceGroup;
        private MediaFrameSourceGroup _shortDepthMediaFrameSourceGroup;

        SensorType _sensorType;
        SensorType _sensorTypeResearch;

        /// <summary>
        /// DepthPvMapper class instance.
        /// </summary>
        private DepthPvMapper _depthPvMapper;
        private bool _isDepthPvMapperInit = false;
#endif

        // Gesture handler
        GestureRecognizer _gestureRecognizer;

        #region UnityMethods

        // Use this for initialization
        async void Start()
        {
            // Initialize gesture handler
            InitializeHandler();

            // Get the material components from quad game objects.
            _pvDepthMaterial = pvDepthGo.GetComponent<MeshRenderer>().material;

            // Start the media frame source groups.
            await StartHoloLensMediaFrameSourceGroups();

        }

        // Update is called once per frame
        void Update()
        {
            UpdateHoloLensMediaFrameSourceGroup();
        }

        async void OnApplicationQuit()
        {
            await StopHoloLensMediaFrameSourceGroup();
        }

        #endregion

        async Task StartHoloLensMediaFrameSourceGroups()
        {
#if ENABLE_WINMD_SUPPORT
            // Plugin doesn't work in the Unity editor
            myText.text = "Initalizing MediaFrameSourceGroups...";

            // PV
            _sensorFrameStreamerPv = new SensorFrameStreamer();
            _sensorType = (SensorType)sensorTypePv;
            _sensorFrameStreamerPv.Enable(_sensorType);

            // Research streams
            _sensorFrameStreamerResearch = new SensorFrameStreamer();
            _sensorTypeResearch = (SensorType)sensorTypeShortDepth;
            _sensorFrameStreamerResearch.Enable(_sensorTypeResearch);

            // Spatial perception
            _spatialPerception = new SpatialPerception();

            // Enable media frame source groups
            // PV
            _pvMediaFrameSourceGroup = new MediaFrameSourceGroup(
                MediaFrameSourceGroupType.PhotoVideoCamera,
                _spatialPerception,
                _sensorFrameStreamerPv);
            _pvMediaFrameSourceGroup.Enable(_sensorType);

            // ToF Depth
            _shortDepthMediaFrameSourceGroup = new MediaFrameSourceGroup(
                MediaFrameSourceGroupType.HoloLensResearchModeSensors,
                _spatialPerception,
                _sensorFrameStreamerResearch);
            _shortDepthMediaFrameSourceGroup.Enable(_sensorTypeResearch);

            // Start media frame source groups
            myText.text = "Starting MediaFrameSourceGroups...";

            // Photo video
            await _pvMediaFrameSourceGroup.StartAsync();

            // ToF Depth
            await _shortDepthMediaFrameSourceGroup.StartAsync();

            _mediaFrameSourceGroupsStarted = true;

            myText.text = "MediaFrameSourceGroups started...";
#endif
        }

        // Get the latest frame from hololens media
        // frame source group -- not needed
        unsafe void UpdateHoloLensMediaFrameSourceGroup()
        {
#if ENABLE_WINMD_SUPPORT
            if (!_mediaFrameSourceGroupsStarted ||
                _pvMediaFrameSourceGroup == null ||
                _shortDepthMediaFrameSourceGroup == null)
            {
                return;
            }

            // Get latest sensor frames
            // Photo video
            SensorFrame latestPvCameraFrame =
                _pvMediaFrameSourceGroup.GetLatestSensorFrame(
                _sensorType);

            // ToF Depth
            SensorFrame latestShortDepthCameraFrame =
                _shortDepthMediaFrameSourceGroup.GetLatestSensorFrame(
                    _sensorTypeResearch);

            // Initialize depth pv mapper class to cache
            // the resulting depth transform.
            if (!_isDepthPvMapperInit && latestShortDepthCameraFrame != null)
            {
                _depthPvMapper = new DepthPvMapper(
                    latestShortDepthCameraFrame);
                _isDepthPvMapperInit = true;
            }

            // Map depth frames to photo video camera
            // with from/to range and specified radius.
            SensorFrame latestPvDepthFrame = _depthPvMapper.MapDepthToPV(
                latestPvCameraFrame,
                latestShortDepthCameraFrame,
                depthRangeFrom,
                depthRangeTo,
                patchRadius);

            // Convert the frame to be unity viewable
            var pvDepthFrame = SoftwareBitmap.Convert(
                latestPvDepthFrame.SoftwareBitmap,
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore);

            // Display the incoming pv frames as a texture.
            // Set texture to the desired renderer
            Destroy(_pvDepthTexture);
            _pvDepthTexture = new Texture2D(
                pvDepthFrame.PixelWidth,
                pvDepthFrame.PixelHeight,
                TextureFormat.BGRA32, false);

            // Get byte array, update unity material with texture (RGBA)
            byte* inBytesPV = GetByteArrayFromSoftwareBitmap(pvDepthFrame);
            _pvDepthTexture.LoadRawTextureData((IntPtr)inBytesPV, pvDepthFrame.PixelWidth * pvDepthFrame.PixelHeight * 4);
            _pvDepthTexture.Apply();
            _pvDepthMaterial.mainTexture = _pvDepthTexture;

            myText.text = "Began streaming sensor frames. Double tap to end streaming.";
#endif
        }

        /// <summary>
        /// Stop the media frame source groups.
        /// </summary>
        /// <returns></returns>
        async Task StopHoloLensMediaFrameSourceGroup()
        {
#if ENABLE_WINMD_SUPPORT
            if (!_mediaFrameSourceGroupsStarted ||
                _pvMediaFrameSourceGroup == null ||
                _shortDepthMediaFrameSourceGroup == null)
            {
                return;
            }

            // Wait for frame source groups to stop.
            await _pvMediaFrameSourceGroup.StopAsync();
            _pvMediaFrameSourceGroup = null;

            await _shortDepthMediaFrameSourceGroup.StopAsync();
            _shortDepthMediaFrameSourceGroup = null;

            // Set to null value
            _sensorFrameStreamerPv = null;
            _sensorFrameStreamerResearch = null;

            // Bool to indicate closing
            _mediaFrameSourceGroupsStarted = false;

            myText.text = "Stopped streaming sensor frames. Okay to exit app.";
#endif
        }

        #region ComImport
        // https://docs.microsoft.com/en-us/windows/uwp/audio-video-camera/imaging
        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        unsafe interface IMemoryBufferByteAccess
        {
            void GetBuffer(out byte* buffer, out uint capacity);
        }
        #endregion

#if ENABLE_WINMD_SUPPORT
        // Get byte array from software bitmap.
        // https://github.com/qian256/HoloLensARToolKit/blob/master/ARToolKitUWP-Unity/Scripts/ARUWPVideo.cs
        unsafe byte* GetByteArrayFromSoftwareBitmap(SoftwareBitmap sb)
        {
            if (sb == null)
                return null;

            SoftwareBitmap sbCopy = new SoftwareBitmap(sb.BitmapPixelFormat, sb.PixelWidth, sb.PixelHeight);
            Interlocked.Exchange(ref sbCopy, sb);
            using (var input = sbCopy.LockBuffer(BitmapBufferAccessMode.Read))
            using (var inputReference = input.CreateReference())
            {
                byte* inputBytes;
                uint inputCapacity;
                ((IMemoryBufferByteAccess)inputReference).GetBuffer(out inputBytes, out inputCapacity);
                return inputBytes;
            }
        }
#endif

        #region TapGestureHandler
        private void InitializeHandler()
        {
            // New recognizer class
            _gestureRecognizer = new GestureRecognizer();

            // Set tap as a recognizable gesture
            _gestureRecognizer.SetRecognizableGestures(GestureSettings.DoubleTap);

            // Begin listening for gestures
            _gestureRecognizer.StartCapturingGestures();

            // Capture on gesture events with delegate handler
            _gestureRecognizer.Tapped += GestureRecognizer_Tapped;

            Debug.Log("Gesture recognizer initialized.");
        }

        // On tapped event, stop all frame source groups
        private void GestureRecognizer_Tapped(TappedEventArgs obj)
        {
            StopHoloLensMediaFrameSourceGroup();
            CloseHandler();
        }

        private void CloseHandler()
        {
            _gestureRecognizer.StopCapturingGestures();
            _gestureRecognizer.Dispose();
        }
        #endregion
    }
}



