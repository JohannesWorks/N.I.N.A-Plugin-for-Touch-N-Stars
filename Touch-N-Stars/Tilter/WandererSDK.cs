using System;
using System.Runtime.InteropServices;

namespace NINA.Plugins.TouchNStars.Tilter
{
    /// <summary>
    /// Wrapper for the Wanderer ETA SDK C API
    /// </summary>
    public static class WandererSDK
    {
        private const string DllName = "libWandererETASDK";

        /// <summary>
        /// Constants
        /// </summary>
        public const int WT_MAX_NUM = 32;           // Maximum ETA devices supported by this SDK
        public const int WT_VERSION_LEN = 32;       // Buffer length for version strings
        public const uint MASK_ETA_POINT_1 = 0x01;  // Mask for point 1 configuration
        public const uint MASK_ETA_POINT_2 = 0x02;  // Mask for point 2 configuration
        public const uint MASK_ETA_POINT_3 = 0x04;  // Mask for point 3 configuration

        /// <summary>
        /// Error codes returned by SDK functions
        /// </summary>
        public enum WTErrorType
        {
            Success = 0,                // Success
            InvalidId = 1,              // Device ID is invalid
            InvalidParameter = 2,       // One or more parameters are invalid
            InvalidState = 3,           // Device is not in correct state for specific API call
            Communication = 4,          // Data communication error such as device has been removed from USB port
            NullPointer = 5             // Caller passes null-pointer parameter which is not expected
        }

        /// <summary>
        /// Version information structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WTVersion
        {
            [MarshalAs(UnmanagedType.U4)]
            public uint Firmware;           // ETA firmware version

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Model;            // Model type
        }

        /// <summary>
        /// ETA configuration structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WTEtaConfig
        {
            [MarshalAs(UnmanagedType.U4)]
            public uint Mask;               // Used by WTETASetConfig() to indicate which field wants to be set

            [MarshalAs(UnmanagedType.R4)]
            public float Position1;         // Request point 1 position

            [MarshalAs(UnmanagedType.R4)]
            public float Position2;         // Request point 2 position

            [MarshalAs(UnmanagedType.R4)]
            public float Position3;         // Request point 3 position
        }

        /// <summary>
        /// ETA status structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WTEtaStatus
        {
            [MarshalAs(UnmanagedType.I4)]
            public int IsMoving;            // Moving state

            [MarshalAs(UnmanagedType.R4)]
            public float CurrentPosition1;  // Point 1 position

            [MarshalAs(UnmanagedType.R4)]
            public float CurrentPosition2;  // Point 2 position

            [MarshalAs(UnmanagedType.R4)]
            public float CurrentPosition3;  // Point 3 position

            [MarshalAs(UnmanagedType.R4)]
            public float Radius;            // Tilter point radius
        }

        /// <summary>
        /// Device scanning and management functions
        /// </summary>

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETAScan(ref int number, [Out] int[] ids);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETAOpen(int id);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETAClose(int id);

        /// <summary>
        /// Configuration functions
        /// </summary>

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETAGetConfig(int id, ref WTEtaConfig config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETASetConfig(int id, ref WTEtaConfig config);

        /// <summary>
        /// Status and information functions
        /// </summary>

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETAGetStatus(int id, ref WTEtaStatus status);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETAGetVersion(int id, ref WTVersion version);

        /// <summary>
        /// Motion control functions
        /// </summary>

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETAOpenCover(int id);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern WTErrorType WTETACloseCover(int id);

        /// <summary>
        /// Utility functions
        /// </summary>

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern WTErrorType WTGetSDKVersion([Out] System.Text.StringBuilder version);
    }
}
