using System;

namespace Addressables_Wrapper.Editor
{
    /// <summary>
    /// A flags enum to select one or more target devices.
    /// </summary>
    [Flags]
    public enum TargetDevice
    {
        Quest = 1 << 0,
        Mobile_Android = 1 << 1,
        Mobile_iOS = 1 << 2,
        PC = 1 << 3
    }
    
    // Create a separate enum for the single target
    public enum BuildTargetDevice
    {
        Quest = TargetDevice.Quest,
        Mobile_Android = TargetDevice.Mobile_Android,
        Mobile_iOS = TargetDevice.Mobile_iOS,
        PC = TargetDevice.PC
    }
    
    public static class BuildTargetDeviceExtensions
    {
        public static string GetDeviceName(this BuildTargetDevice device)
        {
            switch (device)
            {
                case BuildTargetDevice.Quest:
                    return "quest";
                case BuildTargetDevice.Mobile_Android:
                    return "mobile_android";
                case BuildTargetDevice.Mobile_iOS:
                    return "mobile_ios";
                case BuildTargetDevice.PC:
                    return "pc";
                default:
                    return "unknown";
            }
        }
    }
}