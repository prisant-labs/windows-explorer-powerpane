using System;
using System.IO;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;
using PowerPane.Services;
using NM = PowerPane.Interop.NativeMethods;

namespace PowerPane.UI
{
    /// <summary>
    /// Modern folder picker: IFileOpenDialog with FOS_PICKFOLDERS, which looks like the Explorer-style
    /// Open dialog (address bar, search, Quick Access, recent locations) instead of the Windows
    /// 95-era SHBrowseForFolder tree that WinForms' FolderBrowserDialog wraps on .NET Framework 4.8.
    /// Falls back to FolderBrowserDialog if the COM path throws, which covers a locked-down shell or a
    /// future Windows change to these interfaces.
    /// </summary>
    internal static class FolderPicker
    {
        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);
        private const int FDAP_BOTTOM = 0;

        /// <summary>Show the picker and return the chosen folder, or null if canceled.
        /// <paramref name="startFolder"/> is best effort: a null, empty, or missing path is simply not
        /// set, rather than failing the dialog.</summary>
        public static string Pick(string title, string startFolder = null)
        {
            try { return PickModern(title, startFolder); }
            catch (Exception ex)
            {
                Log.Error("modern folder picker failed, falling back to the legacy browser", ex);
                return PickLegacy(title, startFolder);
            }
        }

        private static string PickModern(string title, string startFolder)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                uint opts;
                dialog.GetOptions(out opts);
                dialog.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

                if (StartFolderAnswers(startFolder))
                {
                    var iid = typeof(IShellItem).GUID;
                    IShellItem folder;
                    if (SHCreateItemFromParsingName(startFolder, IntPtr.Zero, ref iid, out folder) == 0 && folder != null)
                    {
                        try { dialog.SetFolder(folder); }
                        finally { Marshal.ReleaseComObject(folder); }
                    }
                }

                AddPins(dialog);

                // Owned by the calling thread's active window, the same way FolderBrowserDialog.ShowDialog()
                // picks an owner when none is given explicitly (see NativeMethods.GetActiveWindow).
                int hr = dialog.Show(NM.GetActiveWindow());
                if (hr == ERROR_CANCELLED) return null;
                if (hr != 0) throw Marshal.GetExceptionForHR(hr);

                IShellItem result;
                dialog.GetResult(out result);
                try
                {
                    IntPtr namePtr;
                    result.GetDisplayName(SIGDN.FILESYSPATH, out namePtr);
                    try { return Marshal.PtrToStringUni(namePtr); }
                    finally { Marshal.FreeCoTaskMem(namePtr); }
                }
                finally { Marshal.ReleaseComObject(result); }
            }
            finally { Marshal.ReleaseComObject(dialog); }
        }

        // The pins go in the picker's own navigation pane, since the pane no longer attaches to
        // PowerPane's own dialogs (see PickerPlaces). Bottom alignment, in pin order, is what WPF's
        // FileDialog.CustomPlaces uses too, so every picker lists them the same way. One pin that
        // fails is skipped: an exception escaping here would send the whole picker to the legacy tree.
        private static void AddPins(IFileOpenDialog dialog)
        {
            var iid = typeof(IShellItem).GUID;
            foreach (var path in PickerPlaces.ForPicker())
            {
                IShellItem item = null;
                try
                {
                    if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item) == 0 && item != null)
                        dialog.AddPlace(item, FDAP_BOTTOM);
                }
                catch (Exception ex) { Log.Error("adding a pin to the folder picker", ex); }
                finally { if (item != null) Marshal.ReleaseComObject(item); }
            }
        }

        // Runs on the UI thread before the dialog opens, so the check is bounded: a start folder on a
        // share that does not answer is skipped after a second instead of freezing the click for the
        // network timeout (prisant-labs/windows-explorer-powerpane#41).
        private static bool StartFolderAnswers(string startFolder)
        {
            bool timedOut;
            return PathStatus.FolderExistsWithin(startFolder, PathStatus.InteractiveProbeMs, out timedOut);
        }

        private static string PickLegacy(string title, string startFolder)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = title ?? "Pick a folder";
                if (StartFolderAnswers(startFolder)) dlg.SelectedPath = startFolder;
                return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

        // No members: this coclass is only ever activated and immediately cast to IFileOpenDialog.
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW { }

        // ---- COM interfaces --------------------------------------------------
        // Declared in exact vtable order (IModalWindow, then IFileDialog, then IFileOpenDialog's own
        // two members): C# interface inheritance does not carry the vtable layout the way COM does, so
        // every base member has to be repeated here in the order the real interface declares it, or
        // every call after the first lands on the wrong slot.
        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // IModalWindow
            [PreserveSig] int Show(IntPtr parent);

            // IFileDialog
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);

            // IFileOpenDialog
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            // The returned pointer is shell-allocated (CoTaskMemAlloc), so it comes back as an IntPtr
            // the caller frees with Marshal.FreeCoTaskMem rather than as a managed BSTR.
            void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        private enum SIGDN : uint
        {
            FILESYSPATH = 0x80058000
        }
    }
}
