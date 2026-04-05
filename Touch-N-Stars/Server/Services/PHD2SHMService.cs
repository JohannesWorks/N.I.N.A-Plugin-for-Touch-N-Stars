using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace PHD2.SHMGuider
{
    // Constants
    public static class SHMGuiderConstants
    {
        public const uint MAX_CAMERAS_SHM = 64;
        public const uint MAX_CAMERA_NAME_LEN = 256;
        public const uint MAX_CAMERA_INSTANCES = 64;
        public const uint INVALID_CAMERA_INDEX = 0xFFFFFFFF;

        public const uint MAX_MOUNTS_SHM = 64;
        public const uint MAX_MOUNT_NAME_LEN = 256;
        public const uint INVALID_MOUNT_INDEX = 0xFFFFFFFF;

        public const string PHD2_CAMERA_SHM_NAME = "/phd2_cameras";
        public const string PHD2_MOUNT_SHM_NAME = "/phd2_mounts";
    }

    // ===== CAMERA STRUCTURES =====

    /// <summary>
    /// Structure representing a single camera entry in shared memory
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct CameraEntry
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN)]
        public byte[] name;

        public CameraEntry(string cameraName)
        {
            name = new byte[(int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN];
            if (!string.IsNullOrEmpty(cameraName))
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(cameraName);
                Array.Copy(nameBytes, name, Math.Min(nameBytes.Length, (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN - 1));
            }
        }

        public string GetName()
        {
            if (name == null) return string.Empty;
            int nullIndex = Array.IndexOf(name, (byte)0);
            if (nullIndex < 0) nullIndex = name.Length;
            return Encoding.UTF8.GetString(name, 0, nullIndex);
        }
    }

    /// <summary>
    /// Structure representing a camera instance (e.g., specific USB camera)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct CameraInstance
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN)]
        public byte[] id;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN)]
        public byte[] display_name;

        public CameraInstance(string instanceId, string displayName)
        {
            id = new byte[(int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN];
            display_name = new byte[(int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN];

            if (!string.IsNullOrEmpty(instanceId))
            {
                byte[] idBytes = Encoding.UTF8.GetBytes(instanceId);
                Array.Copy(idBytes, id, Math.Min(idBytes.Length, (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN - 1));
            }

            if (!string.IsNullOrEmpty(displayName))
            {
                byte[] displayBytes = Encoding.UTF8.GetBytes(displayName);
                Array.Copy(displayBytes, display_name, Math.Min(displayBytes.Length, (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN - 1));
            }
        }

        public string GetId()
        {
            if (id == null) return string.Empty;
            int nullIndex = Array.IndexOf(id, (byte)0);
            if (nullIndex < 0) nullIndex = id.Length;
            return Encoding.UTF8.GetString(id, 0, nullIndex);
        }

        public string GetDisplayName()
        {
            if (display_name == null) return string.Empty;
            int nullIndex = Array.IndexOf(display_name, (byte)0);
            if (nullIndex < 0) nullIndex = display_name.Length;
            return Encoding.UTF8.GetString(display_name, 0, nullIndex);
        }
    }

    /// <summary>
    /// Main shared memory structure containing camera list and selected camera
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CameraListSHM
    {
        public uint version;
        public uint num_cameras;
        public uint selected_camera_index;
        public uint timestamp;
        public uint list_update_counter;
        public uint selected_change_counter;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN)]
        public byte[] selected_camera_id;

        public uint num_instances;
        public uint can_select_camera;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)SHMGuiderConstants.MAX_CAMERA_INSTANCES)]
        public CameraInstance[] instances;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] reserved;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)SHMGuiderConstants.MAX_CAMERAS_SHM)]
        public CameraEntry[] cameras;
    }

    // ===== MOUNT STRUCTURES =====

    /// <summary>
    /// Structure representing a single mount entry in shared memory
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct MountEntry
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)SHMGuiderConstants.MAX_MOUNT_NAME_LEN)]
        public byte[] name;

        public MountEntry(string mountName)
        {
            name = new byte[(int)SHMGuiderConstants.MAX_MOUNT_NAME_LEN];
            if (!string.IsNullOrEmpty(mountName))
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(mountName);
                Array.Copy(nameBytes, name, Math.Min(nameBytes.Length, (int)SHMGuiderConstants.MAX_MOUNT_NAME_LEN - 1));
            }
        }

        public string GetName()
        {
            if (name == null) return string.Empty;
            int nullIndex = Array.IndexOf(name, (byte)0);
            if (nullIndex < 0) nullIndex = name.Length;
            return Encoding.UTF8.GetString(name, 0, nullIndex);
        }
    }

    /// <summary>
    /// Main shared memory structure for mount list
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MountListSHM
    {
        public uint version;
        public uint num_mounts;
        public uint selected_mount_index;
        public uint timestamp;
        public uint list_update_counter;
        public uint selected_change_counter;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)]
        public byte[] reserved;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)SHMGuiderConstants.MAX_MOUNTS_SHM)]
        public MountEntry[] mounts;
    }

    /// <summary>
    /// P/Invoke wrapper for shm_guider library (libshm_guider.so on Linux)
    /// Provides access to shared memory structures for camera and mount equipment
    /// </summary>
    public static class SHMGuiderInterop
    {
        // Platform-specific DLL name
        private const string LibraryName = "libshm_guider.so.1";

        // ===== CAMERA P/INVOKE DECLARATIONS =====

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr shm_camera_init(int create_if_missing);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_camera_cleanup(IntPtr shm, int unlink);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_update_list(IntPtr shm, [In] string[] cameras, uint num_cameras);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_set_selected(IntPtr shm, uint index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint shm_camera_get_selected(IntPtr shm);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_read_list([Out] byte[,] cameras, uint max_cameras);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_read_selected(out uint selected_index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_write_selected(uint index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr shm_camera_get_readonly();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_camera_release_readonly(IntPtr shm);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_read_selected_id([Out] StringBuilder camera_id, int max_len);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_write_selected_id([In] string camera_id);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_update_instances(IntPtr shm, [In] CameraInstance[] instances, uint num_instances);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_read_instances([Out] CameraInstance[] instances, uint max_instances);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_can_select_camera();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_camera_signal_list_changed();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_camera_signal_selected_changed();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_wait_list_changed();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_wait_selected_changed();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_camera_signal_client_request();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_camera_wait_client_request();

        // ===== MOUNT P/INVOKE DECLARATIONS =====

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr shm_mount_init(int create_if_missing);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_mount_cleanup(IntPtr shm, int unlink);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_mount_update_list(IntPtr shm, [In] string[] mounts, uint num_mounts);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_mount_set_selected(IntPtr shm, uint index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint shm_mount_get_selected(IntPtr shm);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_mount_read_list([Out] byte[,] mounts, uint max_mounts);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_mount_read_selected(out uint selected_index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_mount_write_selected(uint index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr shm_mount_get_readonly();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_mount_release_readonly(IntPtr shm);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_mount_signal_list_changed();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_mount_signal_selected_changed();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_mount_wait_list_changed();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_mount_wait_selected_changed();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void shm_mount_signal_client_request();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int shm_mount_wait_client_request();
    }

    // ===== HIGH-LEVEL MANAGED WRAPPERS =====

    /// <summary>
    /// High-level wrapper for camera shared memory operations
    /// Provides convenience methods for common operations
    /// </summary>
    public class CameraManager
    {
        private IntPtr _cameraShm = IntPtr.Zero;

        /// <summary>
        /// Initialize camera shared memory
        /// </summary>
        public bool Initialize(bool createIfMissing = true)
        {
            _cameraShm = SHMGuiderInterop.shm_camera_init(createIfMissing ? 1 : 0);
            return _cameraShm != IntPtr.Zero;
        }

        /// <summary>
        /// Cleanup camera shared memory resources
        /// </summary>
        public void Cleanup(bool unlink = false)
        {
            if (_cameraShm != IntPtr.Zero)
            {
                SHMGuiderInterop.shm_camera_cleanup(_cameraShm, unlink ? 1 : 0);
                _cameraShm = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Update the camera list in shared memory
        /// </summary>
        public bool UpdateCameraList(string[] cameraNames)
        {
            if (_cameraShm == IntPtr.Zero)
                return false;

            if (cameraNames == null || cameraNames.Length > (int)SHMGuiderConstants.MAX_CAMERAS_SHM)
                return false;

            return SHMGuiderInterop.shm_camera_update_list(_cameraShm, cameraNames, (uint)cameraNames.Length) == 0;
        }

        /// <summary>
        /// Set the selected camera by index
        /// </summary>
        public bool SetSelectedCamera(int index)
        {
            if (_cameraShm == IntPtr.Zero)
                return false;

            uint idx = (index < 0) ? SHMGuiderConstants.INVALID_CAMERA_INDEX : (uint)index;
            return SHMGuiderInterop.shm_camera_set_selected(_cameraShm, idx) == 0;
        }

        /// <summary>
        /// Get the selected camera index
        /// </summary>
        public int GetSelectedCamera()
        {
            uint index;
            int result = SHMGuiderInterop.shm_camera_read_selected(out index);
            if (result != 0)
                return -1;
            return (index == SHMGuiderConstants.INVALID_CAMERA_INDEX) ? -1 : (int)index;
        }

        /// <summary>
        /// Read camera list from shared memory
        /// </summary>
        public List<string> ReadCameraList()
        {
            var cameras = new List<string>();
            byte[,] cameraArray = new byte[(int)SHMGuiderConstants.MAX_CAMERAS_SHM, (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN];

            int count = SHMGuiderInterop.shm_camera_read_list(cameraArray, SHMGuiderConstants.MAX_CAMERAS_SHM);
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    byte[] nameBytes = new byte[(int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN];
                    for (int j = 0; j < (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN; j++)
                    {
                        nameBytes[j] = cameraArray[i, j];
                    }
                    int nullIndex = Array.IndexOf(nameBytes, (byte)0);
                    if (nullIndex < 0) nullIndex = nameBytes.Length;
                    string name = Encoding.UTF8.GetString(nameBytes, 0, nullIndex);
                    cameras.Add(name);
                }
            }
            return cameras;
        }

        /// <summary>
        /// Write selected camera index to shared memory
        /// </summary>
        public bool WriteSelectedCamera(int index)
        {
            uint idx = (index < 0) ? SHMGuiderConstants.INVALID_CAMERA_INDEX : (uint)index;
            return SHMGuiderInterop.shm_camera_write_selected(idx) == 0;
        }

        /// <summary>
        /// Get selected camera ID
        /// </summary>
        public string GetSelectedCameraId()
        {
            var sb = new StringBuilder((int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN);
            if (SHMGuiderInterop.shm_camera_read_selected_id(sb, (int)SHMGuiderConstants.MAX_CAMERA_NAME_LEN) == 0)
            {
                return sb.ToString();
            }
            return string.Empty;
        }

        /// <summary>
        /// Set selected camera ID
        /// </summary>
        public bool SetSelectedCameraId(string cameraId)
        {
            return SHMGuiderInterop.shm_camera_write_selected_id(cameraId ?? string.Empty) == 0;
        }

        /// <summary>
        /// Update available camera instances
        /// </summary>
        public bool UpdateCameraInstances(CameraInstance[] instances)
        {
            if (_cameraShm == IntPtr.Zero || instances == null)
                return false;

            return SHMGuiderInterop.shm_camera_update_instances(_cameraShm, instances, (uint)instances.Length) == 0;
        }

        /// <summary>
        /// Read available camera instances
        /// </summary>
        public List<CameraInstance> ReadCameraInstances()
        {
            var instances = new List<CameraInstance>();
            var instanceArray = new CameraInstance[(int)SHMGuiderConstants.MAX_CAMERA_INSTANCES];

            int count = SHMGuiderInterop.shm_camera_read_instances(instanceArray, SHMGuiderConstants.MAX_CAMERA_INSTANCES);
            if (count > 0)
            {
                instances.AddRange(instanceArray.Take(count));
            }
            return instances;
        }

        /// <summary>
        /// Check if instance selection is available
        /// </summary>
        public bool CanSelectCamera()
        {
            return SHMGuiderInterop.shm_camera_can_select_camera() != 0;
        }

        /// <summary>
        /// Signal that camera list has changed
        /// </summary>
        public void SignalListChanged()
        {
            SHMGuiderInterop.shm_camera_signal_list_changed();
        }

        /// <summary>
        /// Signal that selected camera has changed
        /// </summary>
        public void SignalSelectedChanged()
        {
            SHMGuiderInterop.shm_camera_signal_selected_changed();
        }

        /// <summary>
        /// Signal client request for camera change
        /// </summary>
        public void SignalClientRequest()
        {
            SHMGuiderInterop.shm_camera_signal_client_request();
        }
    }

    /// <summary>
    /// High-level wrapper for mount shared memory operations
    /// Provides convenience methods for common operations
    /// </summary>
    public class MountManager
    {
        private IntPtr _mountShm = IntPtr.Zero;

        /// <summary>
        /// Initialize mount shared memory
        /// </summary>
        public bool Initialize(bool createIfMissing = true)
        {
            _mountShm = SHMGuiderInterop.shm_mount_init(createIfMissing ? 1 : 0);
            return _mountShm != IntPtr.Zero;
        }

        /// <summary>
        /// Cleanup mount shared memory resources
        /// </summary>
        public void Cleanup(bool unlink = false)
        {
            if (_mountShm != IntPtr.Zero)
            {
                SHMGuiderInterop.shm_mount_cleanup(_mountShm, unlink ? 1 : 0);
                _mountShm = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Update the mount list in shared memory
        /// </summary>
        public bool UpdateMountList(string[] mountNames)
        {
            if (_mountShm == IntPtr.Zero)
                return false;

            if (mountNames == null || mountNames.Length > (int)SHMGuiderConstants.MAX_MOUNTS_SHM)
                return false;

            return SHMGuiderInterop.shm_mount_update_list(_mountShm, mountNames, (uint)mountNames.Length) == 0;
        }

        /// <summary>
        /// Set the selected mount by index
        /// </summary>
        public bool SetSelectedMount(int index)
        {
            if (_mountShm == IntPtr.Zero)
                return false;

            uint idx = (index < 0) ? SHMGuiderConstants.INVALID_MOUNT_INDEX : (uint)index;
            return SHMGuiderInterop.shm_mount_set_selected(_mountShm, idx) == 0;
        }

        /// <summary>
        /// Get the selected mount index
        /// </summary>
        public int GetSelectedMount()
        {
            uint index;
            int result = SHMGuiderInterop.shm_mount_read_selected(out index);
            if (result != 0)
                return -1;
            return (index == SHMGuiderConstants.INVALID_MOUNT_INDEX) ? -1 : (int)index;
        }

        /// <summary>
        /// Read mount list from shared memory
        /// </summary>
        public List<string> ReadMountList()
        {
            var mounts = new List<string>();
            byte[,] mountArray = new byte[(int)SHMGuiderConstants.MAX_MOUNTS_SHM, (int)SHMGuiderConstants.MAX_MOUNT_NAME_LEN];

            int count = SHMGuiderInterop.shm_mount_read_list(mountArray, SHMGuiderConstants.MAX_MOUNTS_SHM);
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    byte[] nameBytes = new byte[(int)SHMGuiderConstants.MAX_MOUNT_NAME_LEN];
                    for (int j = 0; j < (int)SHMGuiderConstants.MAX_MOUNT_NAME_LEN; j++)
                    {
                        nameBytes[j] = mountArray[i, j];
                    }
                    int nullIndex = Array.IndexOf(nameBytes, (byte)0);
                    if (nullIndex < 0) nullIndex = nameBytes.Length;
                    string name = Encoding.UTF8.GetString(nameBytes, 0, nullIndex);
                    mounts.Add(name);
                }
            }
            return mounts;
        }

        /// <summary>
        /// Write selected mount index to shared memory
        /// </summary>
        public bool WriteSelectedMount(int index)
        {
            uint idx = (index < 0) ? SHMGuiderConstants.INVALID_MOUNT_INDEX : (uint)index;
            return SHMGuiderInterop.shm_mount_write_selected(idx) == 0;
        }

        /// <summary>
        /// Get selected mount ID (read-only from shared memory)
        /// </summary>
        public string GetSelectedMountId()
        {
            // Mount typically doesn't have separate IDs like cameras do
            // Return the name of the selected mount instead
            int selectedIndex = GetSelectedMount();
            if (selectedIndex < 0)
                return string.Empty;
            
            var mounts = ReadMountList();
            if (selectedIndex < mounts.Count)
                return mounts[selectedIndex];
            
            return string.Empty;
        }

        /// <summary>
        /// Signal that mount list has changed
        /// </summary>
        public void SignalListChanged()
        {
            SHMGuiderInterop.shm_mount_signal_list_changed();
        }

        /// <summary>
        /// Signal that selected mount has changed
        /// </summary>
        public void SignalSelectedChanged()
        {
            SHMGuiderInterop.shm_mount_signal_selected_changed();
        }

        /// <summary>
        /// Signal client request for mount change
        /// </summary>
        public void SignalClientRequest()
        {
            SHMGuiderInterop.shm_mount_signal_client_request();
        }
    }

    /// <summary>
    /// Combined equipment manager for both camera and mount
    /// Provides a unified interface for equipment management
    /// </summary>
    public class EquipmentManager
    {
        public CameraManager Camera { get; private set; }
        public MountManager Mount { get; private set; }

        public EquipmentManager()
        {
            Camera = new CameraManager();
            Mount = new MountManager();
        }

        /// <summary>
        /// Initialize both camera and mount shared memory
        /// </summary>
        public bool Initialize(bool createIfMissing = true)
        {
            bool cameraOk = Camera.Initialize(createIfMissing);
            bool mountOk = Mount.Initialize(createIfMissing);
            return cameraOk && mountOk;
        }

        /// <summary>
        /// Cleanup both camera and mount shared memory resources
        /// </summary>
        public void Cleanup(bool unlink = false)
        {
            Camera.Cleanup(unlink);
            Mount.Cleanup(unlink);
        }
    }

    /// <summary>
    /// Backward-compatible wrapper for existing code that uses PHD2SHMService
    /// </summary>
    public class PHD2SHMService
    {
        private readonly CameraManager _cameraManager;
        private readonly MountManager _mountManager;

        public PHD2SHMService()
        {
            _cameraManager = new CameraManager();
            _mountManager = new MountManager();
            
            // Initialize managers
            _cameraManager.Initialize(createIfMissing: true);
            _mountManager.Initialize(createIfMissing: true);
        }

        /// <summary>
        /// Get list of available cameras
        /// </summary>
        public List<string> GetCameraList()
        {
            return _cameraManager.ReadCameraList();
        }

        /// <summary>
        /// Get the index of the currently selected camera
        /// </summary>
        public int GetSelectedCameraIndex()
        {
            return _cameraManager.GetSelectedCamera();
        }

        /// <summary>
        /// Set the selected camera by index
        /// </summary>
        public bool SetSelectedCameraIndex(int index)
        {
            return _cameraManager.SetSelectedCamera(index);
        }

        /// <summary>
        /// Get list of available camera instances
        /// </summary>
        public List<CameraInstance> GetCameraInstances()
        {
            return _cameraManager.ReadCameraInstances();
        }

        /// <summary>
        /// Set selected camera instance by ID
        /// </summary>
        public bool SetSelectedCameraInstanceId(string instanceId)
        {
            return _cameraManager.SetSelectedCameraId(instanceId);
        }

        /// <summary>
        /// Get the selected camera instance
        /// </summary>
        public CameraInstance? GetSelectedCameraInstance()
        {
            var instances = _cameraManager.ReadCameraInstances();
            if (instances.Count == 0)
                return null;
            
            // Return the first instance (would need more context to determine which one is actually selected)
            return instances[0];
        }

        /// <summary>
        /// Get list of camera options (stub - returns empty list)
        /// </summary>
        public List<string> GetCameraOptions()
        {
            // This would need implementation in the underlying CameraManager
            // For now, returning empty list
            return new List<string>();
        }

        /// <summary>
        /// Get camera option value (stub)
        /// </summary>
        public object GetCameraOptionValue(string optionName)
        {
            // This would need implementation in the underlying CameraManager
            return null;
        }

        /// <summary>
        /// Set camera option value (stub)
        /// </summary>
        public bool SetCameraOptionValue(string optionName, object value)
        {
            // This would need implementation in the underlying CameraManager
            return false;
        }

        /// <summary>
        /// Get list of available mounts
        /// </summary>
        public List<string> GetMountList()
        {
            return _mountManager.ReadMountList();
        }

        /// <summary>
        /// Get the index of the currently selected mount
        /// </summary>
        public int GetSelectedMountIndex()
        {
            return _mountManager.GetSelectedMount();
        }

        /// <summary>
        /// Set the selected mount by index
        /// </summary>
        public bool SetSelectedMountIndex(int index)
        {
            return _mountManager.SetSelectedMount(index);
        }

        /// <summary>
        /// Get the ID of the currently selected mount
        /// </summary>
        public string GetSelectedMountId()
        {
            return _mountManager.GetSelectedMountId();
        }

        /// <summary>
        /// Write the selected mount index to shared memory
        /// </summary>
        public bool WriteSelectedMountIndex(int index)
        {
            return _mountManager.WriteSelectedMount(index);
        }

        /// <summary>
        /// Cleanup shared memory resources
        /// </summary>
        public void Cleanup()
        {
            _cameraManager.Cleanup();
            _mountManager.Cleanup();
        }
    }
}
