﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace NRegFreeCom
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// Instance methods are not thread safe.
    /// </remarks>
    public class AssemblySystem
    {
        public string Win32Directory = "Win32";
        public string x64Directory = "x64";
        //NOTE: not sure that using next directoy is good for base (may be some native methods are more proper)
        public string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        ///Windows 7, Windows Server 2008 R2, Windows Vista, and Windows Server 2008: 
        ///  To use this function in an application, call GetProcAddress to retrieve the function's address from Kernel32.dll. 
        /// KB2533623 must be installed on the target platform.
        /// http://support.microsoft.com/kb/2533623
        private static Version _systemSupportsPatch = new Version("6.0.6002");
        private bool _hasPatch = true;

        private Dictionary<IntPtr, string> _dirCookies = new Dictionary<IntPtr, string>();


        /// <summary>
        /// Should managed code to look into <see cref="Win32Directory"/> or <see cref="x64Directory"/> for native library suitable process bitness.
        /// </summary>
        /// <param name="directoryPath"></param>
        /// <returns></returns>
        public string GetAnyCpuPath(string directoryPath)
        {
            //TODO: check not only bits by arch (e.g. COM on ARM or Itanium)
            if (IntPtr.Size == 4)
            {
                return Path.Combine(directoryPath, Win32Directory);
            }
            else if (IntPtr.Size == 8)
            {
                return Path.Combine(directoryPath, x64Directory);
            }
            else throw new NotSupportedException("It is 2033 year or some kind of embedded device. Both are not considered.");
        }


        public string MakePathRooted(string path)
        {
            if (!Path.IsPathRooted(path))
            {
                return Path.Combine(BaseDirectory, path);
            }
            return path;
        }


        public Assembly LoadFrom(string directoryPath, string name)
        {

            string path = Path.Combine(directoryPath, name);
            return LoadFrom(path);
        }

        public Assembly LoadFrom(string path)
        {
            path = normalize(path);//fixes problem with dot C:/.
            IntPtr hModule;
            if (SupportsCustomSearch)
            {
                var flags = LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS |
                            LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR;
                
                hModule = NativeMethods.LoadLibraryEx(path, IntPtr.Zero, flags);
            }
            else
            {
                hModule = NativeMethods.LoadLibraryEx(path, IntPtr.Zero, 0);
            }

            if (hModule == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                var ex = new Win32Exception(error);
                if (error == SYSTEM_ERROR_CODES.ERROR_MOD_NOT_FOUND || error == SYSTEM_ERROR_CODES.ERROR_ENVVAR_NOT_FOUND)
                    throw new System.IO.FileNotFoundException("Failed to find dll", path, ex);
                if (error == SYSTEM_ERROR_CODES.ERROR_BAD_EXE_FORMAT || error == SYSTEM_ERROR_CODES.ERROR_INVALID_PARAMETER)
                    throw new BadImageFormatException("Failed to load dll", path, ex);
                throw ex;
            }
            return new Assembly(hModule, Path.GetFileName(path), path);
        }



        private string normalize(string path)
        {
            return new Uri(path, UriKind.RelativeOrAbsolute).LocalPath;
        }

        /// <summary>
        /// NOTE: this is  unsafe hack for Xp and Vista. Works well on >= Win7
        /// </summary>
        /// <param name="directory"></param>
        /// <seealso href="http://msdn.microsoft.com/en-us/library/ff919712.aspx"/>
        /// <seealso href="http://msdn.microsoft.com/en-us/library/windows/desktop/ms682586.aspx"/>
        public void AddSearchPath(string directory)
        {
            if (SupportsCustomSearch)
            {
                try
                {
                    directory = normalize(directory);
                    IntPtr cookie = NativeMethods.AddDllDirectory(directory);
                    if (cookie == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    _dirCookies.Add(cookie, directory);
                }
                catch (EntryPointNotFoundException ex) // system without patch 
                {
                    Tracing.Source.TraceInformation(string.Format("Failed to AddDllDirectory with next error:{0}", ex));
                    _hasPatch = false;

                    // xp and vista without patch
                    addDllDirectoryToProcessEnvVars(directory);
                    setDllDirectory(directory);
                }
            }
            else
            {
                // xp and vista without patch
                addDllDirectoryToProcessEnvVars(directory);
                setDllDirectory(directory);
            }
        }

        private void setDllDirectory(string directory)
        {
            directory = normalize(directory);
            bool result = NativeMethods.SetDllDirectory(directory);
            if (!result)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // this is last chance usage - security breach because attacker could put its dll in some path
        private void addDllDirectoryToProcessEnvVars(string directory)
        {
            try
            {
                //TODO: manage added paths
                directory = normalize(Path.GetFullPath(directory));
                var paths = getProcessPaths();
                if (!paths.Contains(directory)) //TODO: normalize paths before search, what is impact on dulication?
                {
                    paths += directory + ";";
                    setProcessPaths(paths);
                }
            }
            catch (Exception ex) 
            {
                // consider it OK for for trace analysys (like fusion log analysys)
                Tracing.Source.TraceInformation(string.Format("Failed to add {0} to PATH:{1}", directory,ex));
            }
        }

        private static void setProcessPaths(string paths)
        {
            Environment.SetEnvironmentVariable("PATH", paths, EnvironmentVariableTarget.Process);
        }

        private static string getProcessPaths()
        {
            var paths = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
            if (String.IsNullOrEmpty(paths))
                paths = string.Empty;
            return paths;
        }

        private bool SupportsCustomSearch
        {
            get { return Environment.OSVersion.Version >= _systemSupportsPatch && _hasPatch; }
        }



    }
}
