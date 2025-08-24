using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Threading;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.Desktop.Captures;

/// <summary>
/// Base class for camera capture and frame processing
/// Use OpenCV's IP capture class here!
/// </summary>
public class DesktopConnector : PlatformConnector, IPlatformConnector
{
    public DesktopConnector(string url, ILogger logger, ILocalSettingsService settingsService) : base(url, logger, settingsService)
    {
        Captures = new Dictionary<HashSet<Regex>, Type>();

        // Load all modules
        var dlls = Directory.GetFiles(AppContext.BaseDirectory, "*.dll");
        Logger.LogDebug("Found {DllCount} DLL files in application directory: {DllFiles}", dlls.Length, string.Join(", ", dlls.Select(Path.GetFileName)));
        Captures = LoadAssembliesFromPath(dlls);
        Logger.LogDebug("Loaded {CaptureCount} capture types from assemblies", Captures.Count);
    }

    private Dictionary<HashSet<Regex>, Type> LoadAssembliesFromPath(string[] paths)
    {
        var returnList = new Dictionary<HashSet<Regex>, Type>();

        foreach (var dll in paths)
        {
            try
            {
                var alc = new AssemblyLoadContext(dll, true);
                var loaded = alc.LoadFromAssemblyPath(dll);

                Logger.LogDebug("Scanning assembly '{AssemblyName}' for capture types", loaded.FullName);
                foreach (var type in loaded.GetExportedTypes())
                {
                    Logger.LogDebug("Checking type '{TypeName}' for Capture compatibility", type.FullName);
                    if (typeof(Baballonia.SDK.Capture).IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        // Check if the type has a constructor that takes a string parameter and optional logger (for url and logger)
                        var constructor = type.GetConstructor(new[] { typeof(string), typeof(object) });
                        if (constructor == null)
                        {
                            // Fallback to constructor with just string parameter
                            constructor = type.GetConstructor(new[] { typeof(string) });
                        }
                        if (constructor != null)
                        {
                            // Get the Connections property from the type (instance property)
                            var connectionsProperty = type.GetProperty("Connections", BindingFlags.Public | BindingFlags.Instance);
                            if (connectionsProperty != null && connectionsProperty.PropertyType == typeof(HashSet<Regex>))
                            {
                                // Create a temporary instance to access the Connections property
                                // Handle legacy constructor signatures
                                Capture tempInstance;
                                if (constructor.GetParameters().Length == 2)
                                {
                                    tempInstance = (Capture)Activator.CreateInstance(type, "temp", null)!;
                                }
                                else
                                {
                                    tempInstance = (Capture)Activator.CreateInstance(type, "temp")!;
                                }
                                var connections = (HashSet<Regex>)connectionsProperty.GetValue(tempInstance)!;
                                returnList.Add(connections, type);
                                Logger.LogDebug("Successfully loaded capture type '{CaptureTypeName}' with {PatternCount} connection patterns", type.Name, connections.Count);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("Assembly '{DllPath}' not able to be loaded. Skipping. Error: {ErrorMessage}", dll, e.Message);
            }
        }

        return returnList;
    }

}
