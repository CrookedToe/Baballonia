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
public class DesktopConnector : PlatformConnector
{
    public DesktopConnector(string url, ILogger logger, ILocalSettingsService settingsService) : base(url, logger, settingsService)
    {
        Captures = new Dictionary<HashSet<Regex>, Type>();

        // Load all modules
        var dlls = Directory.GetFiles(AppContext.BaseDirectory, "*.dll");
        Captures = LoadAssembliesFromPath(dlls);
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

                foreach (var type in loaded.GetExportedTypes())
                {
                    if (typeof(Baballonia.SDK.Capture).IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        // Check if the type has a constructor that takes a string parameter (for url)
                        var constructor = type.GetConstructor(new[] { typeof(string) });
                        if (constructor != null)
                        {
                            // Get the Connections property from the type (instance property)
                            var connectionsProperty = type.GetProperty("Connections", BindingFlags.Public | BindingFlags.Instance);
                            if (connectionsProperty != null && connectionsProperty.PropertyType == typeof(HashSet<Regex>))
                            {
                                // Create a temporary instance to access the Connections property
                                var tempInstance = (Capture)Activator.CreateInstance(type, "temp")!;
                                var connections = (HashSet<Regex>)connectionsProperty.GetValue(tempInstance)!;
                                returnList.Add(connections, type);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                //_logger.LogWarning("{error} Assembly not able to be loaded. Skipping.", e.Message);
            }
        }

        return returnList;
    }

}
