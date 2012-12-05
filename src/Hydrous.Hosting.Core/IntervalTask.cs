using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using log4net;

namespace Hydrous.Hosting
{
    interface IIntervalTask : IDisposable
    {
        void Start();
        void Stop();

        /// <summary>
        /// Gets the name of the task
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets a value specifying if the task is currently started or not
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Gets a value specifying if the task is currently executing or not
        /// </summary>
        bool IsExecuting { get; }

        /// <summary>
        /// Gets a value specifying how often the task runs
        /// </summary>
        TimeSpan Interval { get; }

        /// <summary>
        /// Changes the interval of the task
        /// </summary>
        /// <param name="interval"></param>
        void ChangeInterval(TimeSpan interval);
    }

    public class IntervalTask : IIntervalTask
    {
        private bool disposed;
        private Action Operation;
        readonly Timer ExecutionTimer;
        readonly object locker = new object();
        readonly ILog Log;

        public IntervalTask(string name, Action operation, TimeSpan interval)
        {
            if (operation == null)
                throw new ArgumentNullException("operation", "No operation was specified.");

            ExecutionTimer = new Timer(interval.TotalMilliseconds);
            ExecutionTimer.AutoReset = true;
            ExecutionTimer.Elapsed += new ElapsedEventHandler(OnInterval);

            Name = name;
            ChangeInterval(interval);
            Operation = operation;

            Log = LogManager.GetLogger(name);
        }

        public string Name { get; private set; }

        public bool IsExecuting { get; private set; }

        public TimeSpan Interval { get; private set; }

        public bool IsRunning
        {
            get
            {
                lock (locker)
                {
                    return ExecutionTimer.Enabled;
                }
            }
        }

        void OnInterval(object sender, ElapsedEventArgs e)
        {
            // check to make sure we are running
            if (!IsRunning)
                return;

            lock (locker)
            {
                // check to make sure we are running still and not currently executing
                if (!IsRunning || IsExecuting)
                    return;

                IsExecuting = true;
            }

            try
            {
                Operation.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("An unhandled exception was encountered running task.", ex);
            }
            finally
            {
                lock (locker)
                    IsExecuting = false;
            }
        }

        public void Start()
        {
            lock (locker)
            {
                AssertNotDisposed();
                ExecutionTimer.Start();
            }
        }

        private void AssertNotDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        public void Stop()
        {
            if (disposed)
                return;

            lock (locker)
            {
                ExecutionTimer.Stop();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                try
                {
                    using (ExecutionTimer)
                        ExecutionTimer.Stop();
                }
                catch (Exception ex)
                {
                    // swallow any errors, per how dispose should never throw
                    Log.Warn("Failed to dispose and stop execution timer.", ex);
                }
                finally
                {
                    Operation = null;
                }
            }

            disposed = true;
        }

        public void ChangeInterval(TimeSpan interval)
        {
            AssertNotDisposed();

            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("The interval must be greater than 0");

            Interval = interval;
            if (ExecutionTimer != null)
                ExecutionTimer.Interval = Interval.TotalMilliseconds;
        }
    }
}
