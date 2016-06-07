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

	enum Message {
		Result  = 1,
		Connect = 2,
		Listen = 3,
		DeviceAdd = 4,
		DeviceRemove = 5,
		PList = 8,
	};

	class UsbMux
	{
		Socket _sock;
		NetworkStream _stream;

		public UsbMux()
		{
			EndPoint endPoint = new UnixEndPoint ("/var/run/usbmuxd");
			_sock = new Socket (AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			_sock.Connect (endPoint);
			_stream = new NetworkStream(_sock);
		}

		public Device[] ListDevices()
		{
			var plist2 = CreatePListMessage("ListDevices");
			Console.WriteLine(string.Format("sending: {0}", plist2.ToString()));
			WritePListPacket(2, plist2);

			int version;
			Message msg;
			int tag;
			var payload = ReadPacket(out version, out msg, out tag);

			NSData plistData = NSData.FromArray(payload);
			NSPropertyListFormat format = NSPropertyListFormat.Xml;
			NSError error;
			NSDictionary plist = (NSDictionary)NSPropertyListSerialization.PropertyListWithData(plistData, ref format, out error);
			if (plist == null)
				throw new Exception("failed to parse plist");

			var deviceList = (NSArray)plist["DeviceList"];
			var ret = new List<Device>();
			for (uint i = 0; i < deviceList.Count; ++i)
			{
				var device = new NSDictionary(deviceList.ValueAt(i));
				var deviceId = (NSNumber)device["DeviceID"];
				ret.Add(new Device(deviceId.UInt32Value));
				Console.WriteLine(string.Format("device: {0}", device));
			}
			return ret.ToArray();
		}

		void WritePacket(int version, Message message, int tag, byte[] payload = null)
		{
			var binaryWriter = new BinaryWriter(_stream);
			int length = 16;
			if (payload != null)
				length += payload.Length;

			binaryWriter.Write((Int32)length);
			binaryWriter.Write((Int32)version);
			binaryWriter.Write((Int32)message);
			binaryWriter.Write((Int32)tag);

			if (payload != null)
				binaryWriter.Write(payload);
		}

		void WritePListPacket(int tag, NSDictionary plist)
		{
			NSError error;
			var nsData = (NSData)NSPropertyListSerialization.DataWithPropertyList(plist, NSPropertyListFormat.Xml, out error);
			if (nsData == null)
				throw new Exception("failed to serialize plist");

			WritePacket(1, Message.PList, tag, System.Text.Encoding.UTF8.GetBytes(nsData.ToString()));
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
				}

//			WritePacket(stream, 0, Message.Listen, 2);
			}
			catch (Exception e)
			{
				Console.WriteLine(string.Format("fatal exception: {0}", e.ToString()));
			}
		}
	}
}
