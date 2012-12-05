using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using log4net;

namespace Hydrous.Hosting
{
    class DomainHostedObject<T> : IDisposable
        where T : class
    {
        static readonly ILog logger = LogManager.GetLogger(typeof(DomainHostedObject<T>));

        readonly string ApplicationBase;
        readonly string ConfigurationFile;
        readonly string DomainName;
        readonly object locker = new object();

        bool IsDisposed = false;
        private T _Instance;

        public DomainHostedObject(string applicationBase, string configurationFile, string domainName)
        {
            ApplicationBase = applicationBase;
            ConfigurationFile = configurationFile;
            DomainName = domainName;
        }

        private AppDomain Domain { get; set; }

        public T Instance
        {
            get
            {
                if (_Instance != null)
                    return _Instance;

                lock (locker)
                {
                    var domain = CreateDomain();
                    try
                    {
                        var instance = CreateInstance(domain);

                        // once we've created instance, start tracking the domain and instance
                        Domain = domain;
                        _Instance = instance;
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Failed to create hosted instance.", ex);
                        Unload(domain);

                        throw;
                    }
                }

                return _Instance;
            }
        }

        private T CreateInstance(AppDomain domain)
        {
            var type = typeof(T);
            return (T)domain.CreateInstanceAndUnwrap(
                assemblyName: type.Assembly.FullName,
                typeName: type.FullName,
                ignoreCase: true,
                bindingAttr: System.Reflection.BindingFlags.Default,
                binder: null,
                args: null,
                culture: System.Globalization.CultureInfo.CurrentCulture,
                activationAttributes: null,
                securityAttributes: null
            );
        }

        private void Unload(AppDomain domain)
        {
            try
            {
                AppDomain.Unload(domain);
            }
            catch (Exception ex)
            {
                logger.Error(string.Format("Failed to unload app domain '{0}'", domain.FriendlyName), ex);
            }
        }

        AppDomain CreateDomain()
        {
            var setup = AppDomain.CurrentDomain.SetupInformation;
            setup.ShadowCopyFiles = "true";
            setup.ConfigurationFile = ConfigurationFile;
            setup.ApplicationBase = ApplicationBase;

            return AppDomain.CreateDomain(DomainName, null, setup);
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            lock (locker)
            {
                try
                {
                    var d = _Instance as IDisposable;

                    if (d != null)
                        d.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Error("Failed to dispose of instance.", ex);
                }
                finally
                {
                    if (Domain != null)
                        Unload(Domain);
                }
            }
        }
    }
}
