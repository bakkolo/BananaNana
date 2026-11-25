using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace BananaNana.Linux.Evdev;

// Virtual keyboard through /dev/uinput. The compositor treats it as a real keyboard, so the synthetic
// Backspace and Ctrl+V reach Wayland apps too.
public sealed class UInputKeyboard : IDisposable
{
    // ioctl numbers from linux/uinput.h. Linux encodes them as _IOC(dir, type, nr, size):
    // nr bits 0-7, type bits 8-15 ('U' = 0x55), size bits 16-29, dir bits 30-31 (1 = write from userspace).
    //   _IO('U', nr)          = 0x5500 | nr
    //   _IOW('U', nr, int)    = 1<<30 | 4<<16  | 0x5500 | nr
    //   _IOW('U', 3, struct uinput_setup) = 1<<30 | 92<<16 | 0x5500 | 3
    public const ulong UI_DEV_CREATE = 0x5501;     // _IO('U', 1)
    public const ulong UI_DEV_DESTROY = 0x5502;    // _IO('U', 2)
    public const ulong UI_DEV_SETUP = 0x405C5503;  // _IOW('U', 3, struct uinput_setup), 92 bytes
    public const ulong UI_SET_EVBIT = 0x40045564;  // _IOW('U', 100, int)
    public const ulong UI_SET_KEYBIT = 0x40045565; // _IOW('U', 101, int)

    const int O_WRONLY = 0x1, O_NONBLOCK = 0x800, O_CLOEXEC = 0x80000;
    const ushort EvSyn = 0, EvKeyType = 1, SynReport = 0, BusVirtual = 0x06;
    const int MaxKey = 248, SetupSize = 92, NameMax = 80, EventSize = 24, DelayMs = 20, CreateWaitMs = 300;
    const int KeyBackspace = 14, KeyLeftCtrl = 29, KeyV = 47;

    int fd = -1;
    readonly byte[] evBuf = new byte[EventSize];

    public UInputKeyboard()
    {
        fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK | O_CLOEXEC);
        if (fd < 0)
        {
            int err = Marshal.GetLastPInvokeError();
            fd = -1;
            throw err == 13 // EACCES
                ? new InvalidOperationException("Cannot open /dev/uinput (permission denied). Add your user to the 'input' group and install the udev rule (docs/LINUX.md).")
                : new InvalidOperationException($"Cannot open /dev/uinput (errno {err}).");
        }
        try
        {
            Ioctl(UI_SET_EVBIT, EvKeyType);
            Ioctl(UI_SET_EVBIT, EvSyn);
            for (int key = 1; key <= MaxKey; key++) Ioctl(UI_SET_KEYBIT, key);

            // struct uinput_setup { struct input_id { u16 bustype, vendor, product, version }; char name[80]; u32 ff_effects_max; }
            var setup = new byte[SetupSize];
            BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(0), BusVirtual);
            var name = Encoding.UTF8.GetBytes(InputDevices.OwnDeviceName);
            Array.Copy(name, 0, setup, 8, Math.Min(name.Length, NameMax - 1));
            Ioctl(UI_DEV_SETUP, setup);
            Ioctl(UI_DEV_CREATE, 0);
        }
        catch
        {
            Dispose();
            throw;
        }
        Thread.Sleep(CreateWaitMs); // give the compositor time to pick the device up
    }

    public void Tap(int key)
    {
        Send(key, 1);
        Send(key, 0);
    }

    public void Backspace(int count)
    {
        for (int i = 0; i < count; i++) Tap(KeyBackspace);
    }

    public void CtrlV()
    {
        Send(KeyLeftCtrl, 1);
        Tap(KeyV);
        Send(KeyLeftCtrl, 0);
    }

    // One key event followed by a SYN_REPORT, then a short pause so the target app keeps up.
    void Send(int key, int value)
    {
        Emit(EvKeyType, (ushort)key, value);
        Emit(EvSyn, SynReport, 0);
        Thread.Sleep(DelayMs);
    }

    // Writes one struct input_event (timeval left zero, the kernel stamps it).
    void Emit(ushort type, ushort code, int value)
    {
        Array.Clear(evBuf);
        BinaryPrimitives.WriteUInt16LittleEndian(evBuf.AsSpan(16), type);
        BinaryPrimitives.WriteUInt16LittleEndian(evBuf.AsSpan(18), code);
        BinaryPrimitives.WriteInt32LittleEndian(evBuf.AsSpan(20), value);
        if (write(fd, evBuf, EventSize) != EventSize)
            throw new InvalidOperationException($"Write to /dev/uinput failed (errno {Marshal.GetLastPInvokeError()}).");
    }

    void Ioctl(ulong request, long value)
    {
        if (ioctl(fd, request, value) < 0) ThrowIoctl(request);
    }

    void Ioctl(ulong request, byte[] arg)
    {
        if (ioctl(fd, request, arg) < 0) ThrowIoctl(request);
    }

    static void ThrowIoctl(ulong request) =>
        throw new InvalidOperationException($"ioctl 0x{request:X} on /dev/uinput failed (errno {Marshal.GetLastPInvokeError()}).");

    public void Dispose()
    {
        if (fd < 0) return;
        ioctl(fd, UI_DEV_DESTROY, 0L);
        close(fd);
        fd = -1;
    }

    // "libc" is mapped to the glibc soname by the runtime.
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    static extern int ioctl(int fd, ulong request, long value);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    static extern int ioctl(int fd, ulong request, byte[] arg);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    static extern nint write(int fd, byte[] buf, nint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    static extern int close(int fd);
}
