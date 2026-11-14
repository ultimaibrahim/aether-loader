using System;
using System.Collections.Generic;

namespace Aether.Core.SPI
{
    /// <summary>
    /// Service Provider Interface (SPI) locator.
    /// Allows subsystems (Rendering, UI, Registry, Compat) to register implementation contracts dynamically.
    /// </summary>
    public static class AetherServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new();

        public static void Register<TService>(TService instance) where TService : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _services[typeof(TService)] = instance;
        }

        public static TService? Get<TService>() where TService : class
        {
            return _services.TryGetValue(typeof(TService), out var inst) ? (TService)inst : null;
        }

        public static bool TryGet<TService>(out TService service) where TService : class
        {
            if (_services.TryGetValue(typeof(TService), out var inst))
            {
                service = (TService)inst;
                return true;
            }
            service = null!;
            return false;
        }

        public static void Clear() => _services.Clear();
    }
}
