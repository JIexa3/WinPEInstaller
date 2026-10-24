using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinPEInstallerWPF
{
    public record PhysicalDiskInfo(int Number, long SizeBytes, string Model, string BusType);

    /// <summary>
    /// Список физических дисков через Win32 API. Не зависит от языка системы и формата вывода diskpart.
    /// Номер PhysicalDriveN совпадает с номером диска в diskpart и в «Управлении дисками».
    /// Видит и неинициализированные («сырые») диски, и диски без разделов.
    /// </summary>
    public static class PhysicalDisks
    {
        private const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle handle, uint ioControlCode, byte[]? inBuffer, int inBufferSize,
            byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

        private static SafeFileHandle Open(string path) =>
            CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        public static List<PhysicalDiskInfo> Enumerate(int max = 64)
        {
            var list = new List<PhysicalDiskInfo>();
            for (int i = 0; i < max; i++)
            {
                using var h = Open($@"\\.\PhysicalDrive{i}");
                if (h.IsInvalid) continue;

                long size = GetLength(h);
                if (size <= 0) continue; // пустой слот кардридера

                var (model, bus) = GetDescriptor(h);
                list.Add(new PhysicalDiskInfo(i, size, model, bus));
            }
            return list;
        }

        /// <summary>Номер физического диска, на котором лежит том с указанной буквой.</summary>
        public static int? GetDiskNumberOfDrive(char letter)
        {
            using var h = Open($@"\\.\{char.ToUpperInvariant(letter)}:");
            if (h.IsInvalid) return null;

            var buf = new byte[64];
            if (!DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, buf, buf.Length, out _, IntPtr.Zero))
                return null;

            // VOLUME_DISK_EXTENTS: NumberOfDiskExtents (4) + выравнивание (4), затем DISK_EXTENT { DiskNumber, ... }
            if (BitConverter.ToUInt32(buf, 0) < 1) return null;
            return (int)BitConverter.ToUInt32(buf, 8);
        }

        private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;

        private static long GetLength(SafeFileHandle h)
        {
            // IOCTL_DISK_GET_LENGTH_INFO требует права на чтение, а дескриптор открыт с нулевыми правами.
            // GET_DRIVE_GEOMETRY_EX (FILE_ANY_ACCESS) работает без них: DiskSize лежит по смещению 24.
            var geo = new byte[256];
            if (DeviceIoControl(h, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, 0, geo, geo.Length, out _, IntPtr.Zero))
            {
                long size = BitConverter.ToInt64(geo, 24);
                if (size > 0) return size;
            }
            return 0;
        }

        private static (string Model, string Bus) GetDescriptor(SafeFileHandle h)
        {
            // STORAGE_PROPERTY_QUERY: PropertyId = StorageDeviceProperty (0), QueryType = PropertyStandardQuery (0)
            var query = new byte[12];
            var buf = new byte[1024];
            if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, query, query.Length, buf, buf.Length, out _, IntPtr.Zero))
                return ("", "");

            // STORAGE_DEVICE_DESCRIPTOR: VendorIdOffset @12, ProductIdOffset @16, BusType @28
            string vendor = ReadAscii(buf, BitConverter.ToInt32(buf, 12));
            string product = ReadAscii(buf, BitConverter.ToInt32(buf, 16));
            int bus = BitConverter.ToInt32(buf, 28);
            return ((vendor + " " + product).Trim(), BusName(bus));
        }

        private static string ReadAscii(byte[] buf, int offset)
        {
            if (offset <= 0 || offset >= buf.Length) return "";
            int end = offset;
            while (end < buf.Length && buf[end] != 0) end++;
            return Encoding.ASCII.GetString(buf, offset, end - offset).Trim();
        }

        private static string BusName(int bus) => bus switch
        {
            1 => "SCSI",
            3 => "ATA",
            7 => "USB",
            8 => "RAID",
            10 => "SAS",
            11 => "SATA",
            12 => "SD",
            13 => "MMC",
            14 => "Virtual",
            15 => "Virtual",
            17 => "NVMe",
            _ => bus == 0 ? "" : $"Bus{bus}"
        };
    }
}