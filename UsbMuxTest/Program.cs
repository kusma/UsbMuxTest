using System;
using System.IO;
using Fuse.UsbMux;

namespace UsbMuxText
{
	class MainClass
	{

		public static void Main(string[] args)
		{
			try
			{
				AppKit.NSApplication.Init();

				var devices = UsbMux.ListDevices();
				foreach (var d in devices)
				{
					try
					{
						Console.WriteLine(string.Format("device: {0}", d.DeviceID));
						var stream = UsbMux.Connect(d, 1337);
						var streamReader = new StreamReader(stream);
						var hello = streamReader.ReadLine();
						Console.WriteLine("phone says: {0}", hello);
						var streamWriter = new StreamWriter(stream);
						streamWriter.WriteLine("HELLO TO YOU TOO, PHONE!\n");
						streamWriter.Flush();
					}
					catch (FailedToConnectToDevice e)
					{
						Console.WriteLine("failed to connect to device: {0}", e.ToString());
					}
				}

				var deviceListener = new DeviceListener();
				deviceListener.DeviceAttached += (object sender, DeviceListener.DeviceEventArgs a) => Console.WriteLine("attached: " + a.Device.ToString());
				deviceListener.DeviceDetached += (object sender, DeviceListener.DeviceEventArgs a) => Console.WriteLine("detached: " + a.Device.ToString());

				while (true)
					deviceListener.Poll();
			}
			catch (Exception e)
			{
				Console.WriteLine("fatal exception: {0}", e.ToString());
			}
		}
	}
}