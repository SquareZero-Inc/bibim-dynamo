// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using Microsoft.Extensions.DependencyInjection;

namespace BIBIM_MVP
{
    /// <summary>
    /// Dependency Injection container for BIBIM MVP
    /// Centralized service registration and resolution
    /// </summary>
    public static class ServiceContainer
    {
        private static IServiceProvider _serviceProvider;
        private static readonly object _lock = new object();

        /// <summary>
        /// Initialize the DI container with all services
        /// Call this once at application startup
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_serviceProvider != null)
                {
                    // Dynamo may unload/reload the plugin between sessions — reset and reinitialize.
                    _serviceProvider = null;
                }

                var services = new ServiceCollection();

                // Register all services
                RegisterServices(services);

                _serviceProvider = services.BuildServiceProvider();
            }
        }

        /// <summary>
        /// Register all application services
        /// </summary>
        private static void RegisterServices(IServiceCollection services)
        {
            // OSS BYOK: Supabase/Subscription services removed.
            services.AddSingleton<IVersionChecker>(provider => VersionChecker.Instance);

            // Note: ConfigService, HistoryManager, GeminiService use static patterns directly.
        }

        /// <summary>
        /// Get a service instance from the container
        /// </summary>
        public static T GetService<T>() where T : class
        {
            if (_serviceProvider == null)
            {
                throw new InvalidOperationException("ServiceContainer not initialized. Call Initialize() first.");
            }

            return _serviceProvider.GetService<T>();
        }

        /// <summary>
        /// Get a required service (throws if not found)
        /// </summary>
        public static T GetRequiredService<T>() where T : class
        {
            if (_serviceProvider == null)
            {
                throw new InvalidOperationException("ServiceContainer not initialized. Call Initialize() first.");
            }

            return _serviceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// Reset the container (useful for testing)
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                if (_serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _serviceProvider = null;
            }
        }

        /// <summary>
        /// Check if container is initialized
        /// </summary>
        public static bool IsInitialized => _serviceProvider != null;
    }
}
