// Copyright 2007-2010 The Apache Software Foundation.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.

namespace Hydrous.Hosting
{
    using System;
    using Hydrous.Hosting.FileSystem;
    using log4net;
    using Hydrous.Hosting.Internal;

    class HostController : IHostController
    {
        static readonly ILog log = LogManager.GetLogger(typeof(HostController));

        readonly object locker = new object();
        readonly ServiceDirectory Directory;
        readonly IIntervalTask MonitoringTask;

        public HostController(ServiceDirectory directory)
        {
            Directory = directory;
            Status = HostStatus.Created;
            Name = directory.Folder.Name;

            MonitoringTask = new IntervalTask("HostMonitor", CheckStatus, TimeSpan.FromMinutes(1));
        }

        private bool IsDisposed { get; set; }

        private DomainHostedObject<ServiceBootstrapper> Bootstrapper { get; set; }

        public HostStatus Status { get; private set; }

        public string Name { get; private set; }

        private IStartupArguments StartArguments { get; set; }

        void CheckStatus()
        {
            // if running or stopped, don't bother
            if (Status == HostStatus.Running || Status == HostStatus.Stopped) return;

            try
            {
                Initialize();
                Start();
            }
            catch (Exception ex)
            {
                log.Error("Failed to run status check operations on service.", ex);
            }
        }

        public void Initialize()
        {
            lock (locker)
            {
                if (Status == HostStatus.Created)
                {
                    WriteLog("Creating bootstrapper.", log.Debug);
                    Bootstrapper = new DomainHostedObject<ServiceBootstrapper>(
                        applicationBase: Directory.Folder.FullName,
                        configurationFile: Directory.ConfigurationFile.FullName,
                        domainName: "ServiceHost." + Name
                    );

                    WriteLog("Initializing service.");
                    Bootstrapper.Instance.Initialize();

                    Status = HostStatus.Initialized;
                }
            }
        }

        void Start()
        {
            lock (locker)
            {
                if (Status == HostStatus.Initialized)
                {
                    var startingStatus = Status;
                    try
                    {
                        WriteLog("Starting service.");
                        Status = HostStatus.Starting;
                        Bootstrapper.Instance.Start();

                        Status = HostStatus.Running;
                        WriteLog("Service running.");
                    }
                    catch (Exception ex)
                    {
                        log.Error("Failed to start service.", ex);

                        // cleanup our bootstrapper
                        CleanupBootstrapper();
                    }
                }
            }
        }

        public void Start(IStartupArguments arguments)
        {
            StartArguments = arguments;
            MonitoringTask.Start();

            Initialize();
            Start();
        }

        void Stop()
        {
            lock (locker)
            {
                try
                {
                    if (Status == HostStatus.Running)
                    {
                        WriteLog("Stopping service.");
                        Status = HostStatus.Stopping;
                        Bootstrapper.Instance.Stop();
                    }
                }
                finally
                {
                    CleanupBootstrapper();

                    Status = HostStatus.Stopped;
                    WriteLog("Service stopped.");
                }
            }
        }

        public void Stop(IShutdownArguments arguments)
        {
            MonitoringTask.Stop();
            Stop();
        }

        void WriteLog(string message, Action<object> callback = null)
        {
            callback = callback ?? log.Info;

            callback(string.Format("{0} ({1}) - {2}", Name, Status, message));
        }

        public void Dispose()
        {
            // only dispose once
            if (IsDisposed) return;
            IsDisposed = true;

            // run disposal operations
            All(CleanupMonitor, CleanupBootstrapper);
        }

        static void All(params Action[] operations)
        {
            if (operations == null || operations.Length == 0)
                return;

            foreach (var operation in operations)
            {
                try
                {
                    operation();
                }
                catch (Exception ex)
                {
                    log.Warn("Failed to execute disposal operation.", ex);
                }
            }
        }

        void CleanupMonitor()
        {
            if (MonitoringTask != null)
                MonitoringTask.Dispose();
        }

        void CleanupBootstrapper()
        {
            if (Bootstrapper != null)
            {
                WriteLog("Destroying bootstrapper.", log.Debug);

                try
                {
                    Bootstrapper.Dispose();
                }
                catch (Exception ex)
                {
                    log.Error("An error was encountered while disposing of bootstrapper.", ex);
                }
                finally
                {
                    // reset status back to "created" since we no longer have bootstrapper
                    Status = HostStatus.Created;
                    Bootstrapper = null;
                }
            }
        }
    }
}
