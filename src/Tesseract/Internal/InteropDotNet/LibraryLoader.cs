﻿//  Copyright (c) 2014 Andrey Akinshin
//  Project URL: https://github.com/AndreyAkinshin/InteropDotNet
//  Distributed under the MIT License: http://opensource.org/licenses/MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace InteropDotNet
{
    public class LibraryLoader
    {
        private readonly ILibraryLoaderLogic logic;

        private LibraryLoader(ILibraryLoaderLogic logic)
        {
            this.logic = logic;
        }

        private readonly object syncLock = new object();
        private readonly Dictionary<string, IntPtr> loadedAssemblies = new Dictionary<string, IntPtr>();

        public IntPtr LoadLibrary(string fileName, string platformName = null)
        {
            fileName = FixUpLibraryName(fileName);
            lock (syncLock)
            {
                if (!loadedAssemblies.ContainsKey(fileName))
                {
                    if (platformName == null)
                        platformName = SystemManager.GetPlatformName();
                    LibraryLoaderTrace.TraceInformation("Current platform: " + platformName);
                    IntPtr dllHandle = CheckExecutingAssemblyDomain(fileName, platformName);
                    if (dllHandle == IntPtr.Zero)
                        dllHandle = CheckCurrentAppDomain(fileName, platformName);
                    if (dllHandle == IntPtr.Zero)
                        dllHandle = CheckWorkingDirectory(fileName, platformName);

                    if (dllHandle != IntPtr.Zero)
                        loadedAssemblies[fileName] = dllHandle;
                    else
                        throw new DllNotFoundException(string.Format("Failed to find library \"{0}\" for platform {1}.", fileName, platformName));
                }
                return loadedAssemblies[fileName];
            }
        }

        private IntPtr CheckExecutingAssemblyDomain(string fileName, string platformName)
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var baseDirectory = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                LibraryLoaderTrace.TraceInformation("Executing assembly location was empty");
                return IntPtr.Zero;
            }
            return InternalLoadLibrary(baseDirectory, platformName, fileName);
        }

        private IntPtr CheckCurrentAppDomain(string fileName, string platformName)
        {
            var appBase = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(appBase))
            {
                LibraryLoaderTrace.TraceInformation("App domain current domain base was empty");
                return IntPtr.Zero;
            }
            var baseDirectory = Path.GetFullPath(appBase);
            return InternalLoadLibrary(baseDirectory, platformName, fileName);
        }

        private IntPtr CheckWorkingDirectory(string fileName, string platformName)
        {
            var currentDirectory = Environment.CurrentDirectory;
            if (string.IsNullOrEmpty(currentDirectory))
            {
                LibraryLoaderTrace.TraceInformation("Current directory was empty");
                return IntPtr.Zero;
            }
            var baseDirectory = Path.GetFullPath(currentDirectory);
            return InternalLoadLibrary(baseDirectory, platformName, fileName);
        }

        private IntPtr InternalLoadLibrary(string baseDirectory, string platformName, string fileName)
        {
            var fullPath = Path.Combine(baseDirectory, Path.Combine(platformName, fileName));
            return File.Exists(fullPath) ? logic.LoadLibrary(fullPath) : IntPtr.Zero;
        }

        public bool FreeLibrary(string fileName)
        {
            fileName = FixUpLibraryName(fileName);
            lock (syncLock)
            {
                if (!IsLibraryLoaded(fileName))
                {
                    LibraryLoaderTrace.TraceWarning("Failed to free library \"{0}\" because it is not loaded", fileName);
                    return false;
                }
                if (logic.FreeLibrary(loadedAssemblies[fileName]))
                {
                    loadedAssemblies.Remove(fileName);
                    return true;
                }
                return false;
            }
        }

        public IntPtr GetProcAddress(IntPtr dllHandle, string name)
        {
            return logic.GetProcAddress(dllHandle, name);
        }

        public bool IsLibraryLoaded(string fileName)
        {
            fileName = FixUpLibraryName(fileName);
            lock (syncLock)
                return loadedAssemblies.ContainsKey(fileName);
        }

        private string FixUpLibraryName(string fileName)
        {
            return logic.FixUpLibraryName(fileName);

        }

        #region Singleton

        private static LibraryLoader instance;

        public static LibraryLoader Instance
        {
            get
            {
                if (instance == null)
                {
                    var operatingSystem = SystemManager.GetOperatingSystem();
                    switch (operatingSystem)
                    {
                        case OperatingSystem.Windows:
                            LibraryLoaderTrace.TraceInformation("Current OS: Windows");
                            instance = new LibraryLoader(new WindowsLibraryLoaderLogic());
                            break;
                        case OperatingSystem.Unix:
                            LibraryLoaderTrace.TraceInformation("Current OS: Unix");
                            instance = new LibraryLoader(new UnixLibraryLoaderLogic());
                            break;
                        case OperatingSystem.MacOSX:
                            throw new Exception("Unsupported operation system");
                        default:
                            throw new Exception("Unsupported operation system");
                    }
                }
                return instance;
            }
        }

        #endregion
    }
}