using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using Mono.Unix;
using MonoMac.Foundation;

namespace UsbMuxTest
{
	struct Device
	{
		public Device(UInt32 deviceId)
		{
			DeviceID = deviceId;
		}

		public readonly UInt32 DeviceID;

		/*
		TODO?
		public UInt16 ProductID;
		public byte[] SerialNumber;
		public UInt32 Location;
		*/
	}

	enum Message
	{
		Result  = 1,
		Connect = 2,
		Listen = 3,
		DeviceAdd = 4,
		DeviceRemove = 5,
		PList = 8,
	};

	enum Result
	{
		Ok = 0,
		BadCommand = 1,
		BadDevice = 2,
		ConnectionRefused = 3,
		// ??
		BadVersion = 6,
	}

	class UsbMux
	{
		Socket _sock;
		NetworkStream _stream;
		int _tag;

		public UsbMux()
		{
			EndPoint endPoint = new UnixEndPoint("/var/run/usbmuxd");
			_sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			_sock.Connect(endPoint);
			_stream = new NetworkStream(_sock);
		}

		public Device[] ListDevices()
		{
			var plist2 = CreatePListMessage("ListDevices");
			WritePListPacket(plist2);

			int version;
			Message msg;
			int tag;
			var payload = ReadPacket(out version, out msg, out tag);

			NSDictionary plist = PayloadToNSDictionary(payload);

			var deviceList = (NSArray)plist["DeviceList"];
			var ret = new List<Device>();
			for (uint i = 0; i < deviceList.Count; ++i)
			{
				var device = new NSDictionary(deviceList.ValueAt(i));
				var deviceId = (NSNumber)device["DeviceID"];
				ret.Add(new Device(deviceId.UInt32Value));
			}
			return ret.ToArray();
		}

		public Stream Connect(Device device, short port)
		{
			var k = new Object[] { "MessageType", "DeviceID", "PortNumber" };
			var v = new Object[] { "Connect", device.DeviceID, IPAddress.HostToNetworkOrder(port) };
			WritePListPacket(NSDictionary.FromObjectsAndKeys(v, k));

			int version;
			Message msg;
			int tag;
			var payload = ReadPacket(out version, out msg, out tag);
			NSDictionary plist2 = PayloadToNSDictionary(payload);
			var number = (NSNumber)plist2["Number"];
			var result = (Result)number.Int32Value;
			if (result != Result.Ok)
				throw new Exception("failed to connect!");

			var ret = _stream;
			_stream = null;
			return ret;
		}

		void WritePacket(int version, Message message, byte[] payload = null)
		{
			var binaryWriter = new BinaryWriter(_stream);
			int length = 16;
			if (payload != null)
				length += payload.Length;

			binaryWriter.Write((Int32)length);
			binaryWriter.Write((Int32)version);
			binaryWriter.Write((Int32)message);
			binaryWriter.Write((Int32)(++_tag));

			if (payload != null)
				binaryWriter.Write(payload);
		}

		void WritePListPacket(NSDictionary plist)
		{
			NSError error;
			var nsData = (NSData)NSPropertyListSerialization.DataWithPropertyList(plist, NSPropertyListFormat.Xml, out error);
			if (nsData == null)
				throw new Exception("failed to serialize plist");

			WritePacket(1, Message.PList, System.Text.Encoding.UTF8.GetBytes(nsData.ToString()));
		}

		byte[] ReadPacket(out int version, out Message message, out int tag)
		{
			var binaryReader = new BinaryReader(_stream);
			int length = binaryReader.ReadInt32();
			version = binaryReader.ReadInt32();
			message = (Message)binaryReader.ReadInt32();
			tag = binaryReader.ReadInt32();
			return binaryReader.ReadBytes(length - 16);
		}

		static NSDictionary CreatePListMessage(string messageType)
		{
			var k = new Object[] { "MessageType" };
			var v = new Object[] { messageType };
			return NSDictionary.FromObjectsAndKeys(v,k);
		}

		static NSDictionary PayloadToNSDictionary(byte[] payload)
		{
			NSData plistData = NSData.FromArray(payload);
			NSPropertyListFormat format = NSPropertyListFormat.Xml;
			NSError error;
			NSDictionary ret = (NSDictionary)NSPropertyListSerialization.PropertyListWithData(plistData, ref format, out error);
			if (ret == null)
				throw new Exception("failed to parse plist");
			return ret;
		}
	}

	class MainClass
	{

		public static void Main(string[] args)
		{
			try {
				MonoMac.AppKit.NSApplication.Init();

				var usbMux = new UsbMux();
				var devices = usbMux.ListDevices();

				foreach (var d in devices)
				{
					Console.WriteLine(string.Format("device: {0}", d.DeviceID));
					var deviceMux = new UsbMux();
					var stream = deviceMux.Connect(d, 1337);
					var streamReader = new StreamReader(stream);
					var hello = streamReader.ReadLine();
					Console.WriteLine(string.Format("phone says: {0}", hello));
					var streamWriter = new StreamWriter(stream);
					streamWriter.WriteLine("HELLO TO YOU TOO, PHONE!\n");
					streamWriter.Flush();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine(string.Format("fatal exception: {0}", e.ToString()));
			}
		}
	}
}
