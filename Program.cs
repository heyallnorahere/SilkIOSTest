using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Text;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Sdl = Silk.NET.SDL.Sdl;

namespace SilkIOSTest
{
    internal static class Program
    {
        private static readonly Vk vk;
        static Program()
        {
            vk = Vk.GetApi();
        }

        public static void Main()
        {
            Console.WriteLine($"Runtime version: {Environment.Version}");

            /*
            var ctx = Sdl.CreateDefaultContext("__Internal");
            var sdl = new Sdl(ctx);

            if (!ctx.TryGetProcAddress("SDL_GetPlatform", out nint getPlatform))
            {
                Console.WriteLine("Failed to find function");
            }
            else
            {
                Console.WriteLine($"Found SDL_GetPlatform: {getPlatform:X}");
            }
            */

            SdlWindowing.RegisterPlatform();
            using var window = Window.GetView(ViewOptions.DefaultVulkan);

            window.Load += OnLoad;
            window.Run();
        }

        private static unsafe void OnLoad()
        {
            var selectedExtensions = new List<string>();
            {
                uint extensionCount = 0;
                if (vk.EnumerateInstanceExtensionProperties((byte*)null, &extensionCount, null) != Result.Success)
                {
                    throw new InvalidOperationException("Failed to enumerate instance extensions!");
                }

                if (extensionCount > 0)
                {
                    var instanceExtensions = new ExtensionProperties[extensionCount];
                    fixed (ExtensionProperties* extensions = instanceExtensions)
                    {
                        vk.EnumerateInstanceExtensionProperties((byte*)null, &extensionCount, extensions);
                    }

                    for (uint i = 0; i < extensionCount; i++)
                    {
                        var extension = instanceExtensions[i];
                        var extensionName = Marshal.PtrToStringAnsi((nint)extension.ExtensionName);

                        if (extensionName == KhrGetPhysicalDeviceProperties2.ExtensionName) // only need VK_KHR_get_physical_device_properties2
                        {
                            selectedExtensions.Add(extensionName);
                        }
                    }
                }
            }

            // what a nightmare...
            var selectedInstanceExtensions = new byte*[selectedExtensions.Count];
            for (int i = 0; i < selectedExtensions.Count; i++)
            {
                selectedInstanceExtensions[i] = (byte*)Marshal.StringToHGlobalAnsi(selectedExtensions[i]);
            }

            Instance instance;
            fixed (byte* bytes = Encoding.ASCII.GetBytes("iOS test app"))
            {
                var appInfo = new ApplicationInfo
                {
                    SType = StructureType.ApplicationInfo,
                    PNext = null,
                    PApplicationName = bytes,
                    ApplicationVersion = 0,
                    PEngineName = bytes,
                    EngineVersion = 0,
                    ApiVersion = Vk.Version11
                };

                fixed (byte** extensions = selectedInstanceExtensions)
                {
                    var instanceInfo = new InstanceCreateInfo
                    {
                        SType = StructureType.InstanceCreateInfo,
                        PNext = null,
                        Flags = InstanceCreateFlags.None,
                        PApplicationInfo = &appInfo,
                        EnabledLayerCount = 0,
                        PpEnabledLayerNames = null,
                        EnabledExtensionCount = (uint)selectedInstanceExtensions.Length,
                        PpEnabledExtensionNames = extensions
                    };

                    if (vk.CreateInstance(instanceInfo, null, out instance) != Result.Success)
                    {
                        throw new InvalidOperationException("Failed to create instance!");
                    }
                }
            }

            for (int i = 0; i < selectedExtensions.Count; i++)
            {
                Marshal.FreeHGlobal((nint)selectedInstanceExtensions[i]);
            }

            uint deviceCount = 0;
            if (vk.EnumeratePhysicalDevices(instance, ref deviceCount, null) != Result.Success)
            {
                throw new InvalidOperationException("Failed to enumerate devices!");
            }

            if (deviceCount == 0)
            {
                throw new InvalidOperationException("No devices found!");
            }

            var devices = new PhysicalDevice[deviceCount];
            fixed (PhysicalDevice* devicePointer = devices)
            {
                vk.EnumeratePhysicalDevices(instance, ref deviceCount, devicePointer);
            }

            for (int i = 0; i < devices.Length; i++)
            {
                vk.GetPhysicalDeviceProperties(devices[i], out PhysicalDeviceProperties properties);
                var deviceName = Marshal.PtrToStringAnsi((nint)properties.DeviceName);
                Console.WriteLine($"Device {i}: {deviceName}");
            }

            var physicalDevice = devices[0]; // lets just use the first device
            vk.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties selectedProperties);

            var selectedDeviceName = Marshal.PtrToStringAnsi((nint)selectedProperties.DeviceName);
            Console.WriteLine($"Selected device: {selectedDeviceName}");

            uint queueFamilyCount = 0;
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);

            if (queueFamilyCount == 0)
            {
                throw new InvalidOperationException("No queue families found!");
            }

            var queueFamilyProperties = new QueueFamilyProperties[queueFamilyCount];
            fixed (QueueFamilyProperties* properties = queueFamilyProperties)
            {
                vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, properties);
            }

            var desiredQueues = new HashSet<QueueFlags>
            {
                QueueFlags.GraphicsBit,
                QueueFlags.TransferBit,
                QueueFlags.ComputeBit
            };

            var foundQueueFamilies = new Dictionary<QueueFlags, uint>();
            for (uint i = 0; i < queueFamilyCount; i++)
            {
                var properties = queueFamilyProperties[i];
                foreach (var desiredQueue in desiredQueues)
                {
                    if (foundQueueFamilies.ContainsKey(desiredQueue) || !properties.QueueFlags.HasFlag(desiredQueue))
                    {
                        continue;
                    }

                    foundQueueFamilies.Add(desiredQueue, i);
                    if (foundQueueFamilies.Count == desiredQueues.Count)
                    {
                        break;
                    }
                }
            }

            string foundQueues = string.Empty;
            foreach (var familyType in foundQueueFamilies.Keys)
            {
                if (foundQueues.Length > 0)
                {
                    foundQueues += ", ";
                }

                foundQueues += familyType.ToString();
            }

            Console.WriteLine($"Found queue families: {foundQueues}");

            var selectedQueueFamilies = new HashSet<uint>(foundQueueFamilies.Values);
            var sharingFamilies = new uint[selectedQueueFamilies.Count];
            var deviceQueueInfo = new DeviceQueueCreateInfo[selectedQueueFamilies.Count];
            int currentDeviceQueueIndex = 0;

            float priority = 1f;
            foreach (uint queueFamily in selectedQueueFamilies)
            {
                int currentIndex = currentDeviceQueueIndex++;

                sharingFamilies[currentIndex] = queueFamily;
                deviceQueueInfo[currentIndex] = new DeviceQueueCreateInfo
                {
                    SType = StructureType.DeviceQueueCreateInfo,
                    PNext = null,
                    Flags = DeviceQueueCreateFlags.None,
                    QueueFamilyIndex = queueFamily,
                    QueueCount = 1,
                    PQueuePriorities = &priority
                };
            }

            {
                uint extensionCount = 0;
                if (vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, null) != Result.Success)
                {
                    throw new InvalidOperationException("Failed to enumerate device extensions!");
                }

                selectedExtensions.Clear();
                if (extensionCount > 0)
                {
                    var deviceExtensions = new ExtensionProperties[extensionCount];
                    fixed (ExtensionProperties* extensions = deviceExtensions)
                    {
                        vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, extensions);
                    }

                    for (uint i = 0; i < extensionCount; i++)
                    {
                        var extension = deviceExtensions[i];
                        var extensionName = Marshal.PtrToStringAnsi((nint)extension.ExtensionName);

                        // https://registry.khronos.org/vulkan/specs/1.3-extensions/man/html/VK_KHR_portability_subset.html
                        if (extensionName == "VK_KHR_portability_subset")
                        {
                            selectedExtensions.Add(extensionName);
                        }
                    }
                }
            }

            // eugh
            var selectedDeviceExtensions = new byte*[selectedExtensions.Count];
            for (int i = 0; i < selectedExtensions.Count; i++)
            {
                selectedDeviceExtensions[i] = (byte*)Marshal.StringToHGlobalAnsi(selectedExtensions[i]);
            }

            Device device;
            fixed (DeviceQueueCreateInfo* queueInfo = deviceQueueInfo)
            {
                fixed (byte** deviceExtensions = selectedDeviceExtensions)
                {
                    vk.GetPhysicalDeviceFeatures(physicalDevice, out PhysicalDeviceFeatures features);
                    var deviceInfo = new DeviceCreateInfo
                    {
                        SType = StructureType.DeviceCreateInfo,
                        PNext = null,
                        Flags = 0,
                        QueueCreateInfoCount = (uint)deviceQueueInfo.Length,
                        PQueueCreateInfos = queueInfo,
                        EnabledLayerCount = 0,
                        PpEnabledLayerNames = null,
                        EnabledExtensionCount = (uint)selectedDeviceExtensions.Length,
                        PpEnabledExtensionNames = deviceExtensions,
                        PEnabledFeatures = &features
                    };

                    if (vk.CreateDevice(physicalDevice, deviceInfo, null, out device) != Result.Success)
                    {
                        throw new InvalidOperationException("Failed to create logical device!");
                    }
                }
            }

            // lets hope this works...
            Image image;
            fixed (uint* imageSharingFamilies = sharingFamilies)
            {
                var imageInfo = new ImageCreateInfo
                {
                    SType = StructureType.ImageCreateInfo,
                    PNext = null,
                    Flags = ImageCreateFlags.None,
                    ImageType = ImageType.Type2D,
                    Format = Format.R8G8B8A8Unorm, // should be supported on most platforms
                    Extent = new Extent3D
                    {
                        Width = 64,
                        Height = 64,
                        Depth = 1
                    },
                    MipLevels = 1,
                    ArrayLayers = 1,
                    Samples = SampleCountFlags.Count1Bit,
                    Tiling = ImageTiling.Optimal,
                    Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                    SharingMode = selectedQueueFamilies.Count > 1 ? SharingMode.Concurrent : SharingMode.Exclusive,
                    QueueFamilyIndexCount = (uint)sharingFamilies.Length,
                    PQueueFamilyIndices = imageSharingFamilies
                };

                if (vk.CreateImage(device, imageInfo, null, out image) != Result.Success)
                {
                    throw new InvalidOperationException("Failed to create device image!");
                }
            }

            vk.DestroyImage(device, image, null);
            vk.DestroyDevice(device, null);
            vk.DestroyInstance(instance, null);
        }
    }
}