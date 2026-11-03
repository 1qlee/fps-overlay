using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace FpsOverlay
{
    public readonly record struct HardwareStats(
        string? CpuName,
        float? CpuTempC,
        float? CpuLoadPct,
        string? GpuName,
        float? GpuTempC,
        float? GpuLoadPct);

    /// <summary>
    /// Reads CPU and GPU temperature/load through LibreHardwareMonitorLib.
    /// CPU temperatures come from model-specific registers, which need a kernel
    /// driver (PawnIO in current LibreHardwareMonitor releases) and admin rights.
    /// GPU readings come from the vendor APIs (NVAPI, ADL, Intel IGCL).
    /// </summary>
    public sealed class HardwareMonitor : IDisposable
    {
        private readonly Computer _computer;
        private readonly object _lock = new();

        public HardwareMonitor()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
            };
            _computer.Open();
        }

        /// <summary>Polls the sensors. Takes a few tens of ms, so call it off the UI thread.</summary>
        public HardwareStats Read()
        {
            lock (_lock)
            {
                foreach (var hw in _computer.Hardware)
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware)
                        sub.Update();
                }

                var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);

                // Prefer a discrete GPU over integrated graphics.
                var gpu = _computer.Hardware
                    .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    .OrderBy(h => h.HardwareType == HardwareType.GpuIntel ? 1 : 0)
                    .FirstOrDefault();

                return new HardwareStats(
                    cpu?.Name,
                    cpu is null ? null : Pick(AllSensors(cpu), SensorType.Temperature,
                        "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "Core Max", "Core Average"),
                    cpu is null ? null : Pick(AllSensors(cpu), SensorType.Load, "CPU Total"),
                    gpu?.Name,
                    gpu is null ? null : Pick(AllSensors(gpu), SensorType.Temperature, "GPU Core", "GPU Hot Spot"),
                    gpu is null ? null : Pick(AllSensors(gpu), SensorType.Load, "GPU Core", "D3D 3D"));
            }
        }

        private static IEnumerable<ISensor> AllSensors(IHardware hw) =>
            hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors));

        /// <summary>
        /// Returns the first sensor matching one of the preferred names (in order),
        /// falling back to the highest reading of that type.
        /// </summary>
        private static float? Pick(IEnumerable<ISensor> sensors, SensorType type, params string[] preferredNames)
        {
            // Unsupported temperature sensors often report 0, so ignore those.
            var ofType = sensors
                .Where(s => s.SensorType == type && s.Value.HasValue)
                .Where(s => type != SensorType.Temperature || s.Value > 0)
                .ToList();

            foreach (var name in preferredNames)
            {
                var match = ofType.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return match.Value;
            }

            return ofType.Count > 0 ? ofType.Max(s => s.Value) : null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try { _computer.Close(); } catch { }
            }
        }
    }
}
