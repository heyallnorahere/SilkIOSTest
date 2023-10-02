using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;
using Silk.NET.Vulkan;
using System.Text;
using System;
using System.Runtime.InteropServices;

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
            var options = WindowOptions.DefaultVulkan with
            {
                Title = "Silk.NET iOS test", // don't even know if this matters on ios sdl lmao
            };

            SdlWindowing.RegisterPlatform();
            using var window = Window.Create(options);

            window.Load += OnLoad;
            window.Run();
        }

        private static unsafe void OnLoad()
        {
            Instance instance;
            fixed (byte* bytes = Encoding.ASCII.GetBytes("IOS test app"))
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

                var instanceInfo = new InstanceCreateInfo
                {
                    SType = StructureType.InstanceCreateInfo,
                    PNext = null,
                    Flags = InstanceCreateFlags.None,
                    PApplicationInfo = &appInfo,
                    EnabledLayerCount = 0,
                    PpEnabledLayerNames = null,
                    EnabledExtensionCount = 0,
                    PpEnabledExtensionNames = null
                };

                if (vk.CreateInstance(instanceInfo, null, out instance) != Result.Success)
                {
                    throw new InvalidOperationException("Failed to create instance!");
                }
            }

            uint deviceCount = 0;
            if (vk.EnumeratePhysicalDevices(instance, ref deviceCount, null) != Result.Success)
            {
                throw new InvalidOperationException("Failed to enumerate devices!");
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

            vk.DestroyInstance(instance, null);
        }
    }
}