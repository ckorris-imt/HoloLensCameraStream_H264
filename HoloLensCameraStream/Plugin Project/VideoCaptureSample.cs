//  
// Copyright (c) 2017 Vulcan, Inc. All rights reserved.  
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Perception.Spatial;
using Windows.Graphics.Imaging;
using Windows.Media.Capture.Frames;
using Windows.Graphics.DirectX.Direct3D11;

namespace HoloLensCameraStream
{
    public class VideoCaptureSample : IDisposable
    {
        /// <summary>
        /// How many bytes are in the frame.
        /// There are four bytes per pixel, times the width and height of the bitmap.
        /// </summary>
        public int dataLength
        {
            get
            {
                switch (pixelFormat)
                {
                    case CapturePixelFormat.BGRA32:
                        return FrameWidth * FrameHeight * 4;
                    case CapturePixelFormat.NV12:
                        return (FrameWidth * FrameHeight * 6) / 4;
                    default:
                        return -1;
                }
            }
        }

        /// <summary>
        /// Note: This method has not been written. Help us out on GitHub!
        /// Will be true if the HoloLens knows where it is and is tracking.
        /// Indicates that obtaining the matrices will be successful.
        /// </summary>
        public bool hasLocationData
        {
            get
            {
                //TODO: Return if location data exists.
                throw new NotImplementedException();
            }
        }

        internal IDirect3DSurface d3dSurface;

        /// <summary>
        /// The format of the frames that the bitmap stream is sending.
        /// </summary>
        public CapturePixelFormat pixelFormat { get; private set; }

        /// <summary>
        /// The camera intrinsics
        /// </summary>
        public CameraIntrinsics cameraIntrinsics
        {
            get
            {
                Windows.Media.Devices.Core.CameraIntrinsics mediaFrameIntrinsics = frameReference.VideoMediaFrame.CameraIntrinsics;
                if (mediaFrameIntrinsics == null)
                {
                    return null;
                }

                return new CameraIntrinsics(mediaFrameIntrinsics);
            }
        }

        public int FrameWidth { get; private set; }
        public int FrameHeight { get; private set; }

        public TimeSpan FrameTime { get; private set; }

        //Internal members

        internal SpatialCoordinateSystem worldOrigin { get; private set; }

        internal SoftwareBitmap bitmap { get; private set; }

        internal bool isBitmapCopied { get; private set; }

        //Private members

        MediaFrameReference frameReference;

        internal VideoCaptureSample(MediaFrameReference frameReference, SpatialCoordinateSystem worldOrigin)
        {
            if (frameReference == null)
            {
                throw new ArgumentNullException("frameReference.");
            }

            this.frameReference = frameReference;
            this.worldOrigin = worldOrigin;
            FrameTime = frameReference.SystemRelativeTime.Value;

            bitmap = frameReference.VideoMediaFrame.SoftwareBitmap;
            d3dSurface = frameReference.VideoMediaFrame.Direct3DSurface;

            if (bitmap != null)
            {
                pixelFormat = ConvertBitmapPixelFormatToCapturePixelFormat(bitmap.BitmapPixelFormat);
                FrameWidth = bitmap.PixelWidth;
                FrameHeight = bitmap.PixelHeight;
            }
            else
            {
                FrameWidth = d3dSurface.Description.Width;
                FrameHeight = d3dSurface.Description.Height;
                pixelFormat = CapturePixelFormat.Unknown; //TODO: Fix later.
            }
        }

        /// <summary>
        /// If you need safe, long term control over the image bytes in this frame, they will need to be
        /// copied. You need to supply a byte[] to copy them into. It is best to pre-allocate and reuse
        /// this byte array to minimize unecessarily high memory ceiling or unnecessary garbage collections.
        /// </summary>
        /// <param name="byteBuffer">A byte array with a length the size of VideoCaptureSample.dataLength</param>
        public void CopyRawImageDataIntoBuffer(byte[] byteBuffer)
        {
            //Here is a potential way to get direct access to the buffer:
            //http://stackoverflow.com/questions/25481840/how-to-change-mediacapture-to-byte

            if (byteBuffer == null)
            {
                throw new ArgumentNullException("byteBuffer");
            }

            if (byteBuffer.Length < dataLength)
            {
                throw new IndexOutOfRangeException("Your byteBuffer is not big enough." +
                    " Please use the VideoCaptureSample.dataLength property to allocate a large enough array.");
            }

            bitmap.CopyToBuffer(byteBuffer.AsBuffer());
            isBitmapCopied = true;
        }


        public void CopyRawImageDataIntoBuffer(List<byte> byteBuffer)
        {
            throw new NotSupportedException("This method is not yet supported with a List<byte>. Please provide a byte[] instead.");
        }

        public bool GetIDirect3DSurfaceAsObject(out object d3dAsObject)
        {
            d3dAsObject = d3dSurface;
            return d3dSurface != null;
        }

        /// <summary>
        /// This returns the transform matrix at the time the photo was captured, if location data if available.
        /// If it's not, that is probably an indication that the HoloLens is not tracking and its location is not known.
        /// It could also mean the VideoCapture stream is not running.
        /// If location data is unavailable then the camera to world matrix will be set to the identity matrix.
        /// </summary>
        /// <param name="matrix">The transform matrix used to convert between coordinate spaces.
        /// The matrix will have to be converted to a Unity matrix before it can be used by methods in the UnityEngine namespace.
        /// See https://forum.unity3d.com/threads/locatable-camera-in-unity.398803/ for details.</param>
        public bool TryGetCameraToWorldMatrix(out float[] outMatrix)
        {
            // from https://github.com/qian256/HoloLensARToolKit/blob/bef36a89f191ab7d389d977c46639376069bbed6/HoloLensARToolKit/Assets/ARToolKitUWP/Scripts/ARUWPVideo.cs#L603
            if (worldOrigin == null)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            SpatialCoordinateSystem cameraCoordinateSystem = frameReference.CoordinateSystem;
            if (cameraCoordinateSystem == null)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            Matrix4x4? cameraCoordsToUnityCoordsMatrix = cameraCoordinateSystem.TryGetTransformTo(worldOrigin);
            if (cameraCoordsToUnityCoordsMatrix == null)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            Matrix4x4 cameraCoordsToUnityCoords = Matrix4x4.Transpose(cameraCoordsToUnityCoordsMatrix.Value);

            // Change from right handed coordinate system to left handed UnityEngine
            cameraCoordsToUnityCoords.M31 *= -1f;
            cameraCoordsToUnityCoords.M32 *= -1f;
            cameraCoordsToUnityCoords.M33 *= -1f;
            cameraCoordsToUnityCoords.M34 *= -1f;

            outMatrix = ConvertMatrixToFloatArray(cameraCoordsToUnityCoords);

            return true;
        }

        /// <summary>
        /// This returns the projection matrix at the time the photo was captured, if location data if available.
        /// If it's not, that is probably an indication that the HoloLens is not tracking and its location is not known.
        /// It could also mean the VideoCapture stream is not running.
        /// If location data is unavailable then the projecgtion matrix will be set to the identity matrix.
        /// </summary>
        /// <param name="matrix">The projection matrix used to match the true camera projection.
        /// The matrix will have to be converted to a Unity matrix before it can be used by methods in the UnityEngine namespace.
        /// See https://forum.unity3d.com/threads/locatable-camera-in-unity.398803/ for details.</param>
        public bool TryGetProjectionMatrix(out float[] outMatrix)
        {
            CameraIntrinsics cameraIntrinsics = this.cameraIntrinsics;
            if (cameraIntrinsics == null)
            {
                outMatrix = GetIdentityMatrixFloatArray();
                return false;
            }

            Matrix4x4 projectionMatrix = ConvertCameraIntrinsicsToProjectionMatrix(cameraIntrinsics, 0.1f, 1000f);

            // Transpose matrix to match expected Unity format
            projectionMatrix = Matrix4x4.Transpose(projectionMatrix);

            outMatrix = ConvertMatrixToFloatArray(projectionMatrix);

            return true;
        }

        /// <summary>
        /// Note: This method hasn't been written yet. Help us out on GitHub!
        /// </summary>
        /// <param name="targetTexture"></param>
        public void UploadImageDataToTexture(object targetTexture)
        {
            //TODO: Figure out how to use a Texture2D in a plugin.
            throw new NotSupportedException("I'm not sure how to use a Texture2D within this plugin.");
        }

        /// <summary>
        /// When done with the VideoCapture class, you will need to dispose it to release unmanaged memory.
        /// </summary>
        public void Dispose()
        {
            bitmap?.Dispose();
            d3dSurface?.Dispose(); //Shouldn't be necessary, but just in case.
            frameReference.Dispose();
        }

        // from https://strawlab.org/2011/11/05/augmented-reality-with-OpenGL/
        private Matrix4x4 ConvertCameraIntrinsicsToProjectionMatrix(CameraIntrinsics cameraIntrinsics, float near, float far)
        {
            Matrix4x4 projectionMatrix = new Matrix4x4();
            projectionMatrix.M11 = 2.0f * cameraIntrinsics.FocalLengthX / cameraIntrinsics.ImageWidth;
            projectionMatrix.M31 = 1.0f - 2.0f * cameraIntrinsics.PrincipalPointX / cameraIntrinsics.ImageWidth;
            projectionMatrix.M22 = 2.0f * cameraIntrinsics.FocalLengthY / cameraIntrinsics.ImageHeight;
            projectionMatrix.M32 = -1.0f + 2.0f * cameraIntrinsics.PrincipalPointY / cameraIntrinsics.ImageHeight;
            projectionMatrix.M33 = -(far + near) / (far - near);
            projectionMatrix.M43 = -2.0f * far * near / (far - near);
            projectionMatrix.M34 = -1.0f;

            return projectionMatrix;
        }

        private float[] ConvertMatrixToFloatArray(Matrix4x4 matrix)
        {
            return new float[16] {
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44 };
        }

        private float[] ConvertByteArrayToFloatArray(byte[] values)
        {
            if (values.Length < 4 || values.Length % 4 != 0)
            {
                throw new ArgumentException("Expected byte array to at least 4 bytes and divisible by 4. Array was " + values.Length + " bytes");
            }

            float[] outputArr = new float[values.Length / 4];
            for (int i = 0; i < outputArr.Length; i++)
            {
                outputArr[i] = BitConverter.ToSingle(values, i * 4);
            }
            return outputArr;
        }

        private Matrix4x4 ConvertByteArrayToMatrix4x4(byte[] matrixAsBytes)
        {
            if (matrixAsBytes == null)
            {
                throw new ArgumentNullException("matrixAsBytes");
            }

            if (matrixAsBytes.Length != 64)
            {
                throw new Exception("Cannot convert byte[] to Matrix4x4. Size of array should be 64, but it is " + matrixAsBytes.Length);
            }

            var m = matrixAsBytes;
            return new Matrix4x4(
                BitConverter.ToSingle(m, 0),
                BitConverter.ToSingle(m, 4),
                BitConverter.ToSingle(m, 8),
                BitConverter.ToSingle(m, 12),
                BitConverter.ToSingle(m, 16),
                BitConverter.ToSingle(m, 20),
                BitConverter.ToSingle(m, 24),
                BitConverter.ToSingle(m, 28),
                BitConverter.ToSingle(m, 32),
                BitConverter.ToSingle(m, 36),
                BitConverter.ToSingle(m, 40),
                BitConverter.ToSingle(m, 44),
                BitConverter.ToSingle(m, 48),
                BitConverter.ToSingle(m, 52),
                BitConverter.ToSingle(m, 56),
                BitConverter.ToSingle(m, 60));
        }

        static CapturePixelFormat ConvertBitmapPixelFormatToCapturePixelFormat(BitmapPixelFormat format)
        {
            switch (format)
            {
                case BitmapPixelFormat.Bgra8:
                    return CapturePixelFormat.BGRA32;
                case BitmapPixelFormat.Nv12:
                    return CapturePixelFormat.NV12;
                default:
                    return CapturePixelFormat.Unknown;
            }
        }

        static byte[] GetIdentityMatrixByteArray()
        {
            return new byte[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        }

        static float[] GetIdentityMatrixFloatArray()
        {
            return new float[] { 1f, 0, 0, 0, 0, 1f, 0, 0, 0, 0, 1f, 0, 0, 0, 0, 1f };
        }
    }
}