using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Unix;
using MonoMac.Foundation;

namespace UsbMuxTest
{
	class FailedToConnectToDevice : Exception
	{
		public readonly Device Device;

		public FailedToConnectToDevice(string message, Device device)
			: base("Failed to connect to " + device + " with " + message)
		{
			Device = device;
		}
	}

	class FailedToConnectToUSBMux : Exception
	{
		public FailedToConnectToUSBMux()
			: base("Failed to connect to usbmux.")
		{
		}
	}

	class FailedToParsePlist : Exception
	{
		public FailedToParsePlist()
			: base("Failed to parse plist.")
		{
		}
	}

	class FailedToSendMessage : Exception
	{
		public FailedToSendMessage() : base("Failed to send message to usbmux.")
		{
		}
	}

	class FailedToParseResponse : Exception
	{
		public FailedToParseResponse()
			: base("Failed to parse response from usbmux.")
		{
		}
	}

	public class Device
	{
		public Device(uint deviceId, ushort productId, byte[] serialNumber, uint location)
		{
			DeviceID = deviceId;
			ProductID = productId;
			SerialNumber = serialNumber;
			Location = location;
		}

		public readonly uint DeviceID;
		public readonly ushort ProductID;
		public readonly byte[] SerialNumber;
		public readonly uint Location;

		public override string ToString()
		{
			return "{ DeviceID: " + DeviceID
				+ ", ProductID: " + ProductID
				+ ", SerialNumber: " + BitConverter.ToString(SerialNumber)
				+ ", Location: " + Location
				+ " }";
		}
	}

	enum Message
	{
		Result = 1,
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

	class MessageData
	{
		public readonly int Version;
		public readonly Message Message;
		public readonly int Tag;
		public readonly byte[] Payload;

		MessageData(int version, NSDictionary payload)
			: this(version, Message.PList, 0, Serialize(payload))
		{
		}

		MessageData(int version, Message message, int tag, byte[] payload)
		{
			Version = version;
			Message = message;
			Tag = tag;
			Payload = payload;
		}

		static byte[] Serialize(NSDictionary plist)
		{
			NSError error;
			var nsData = NSPropertyListSerialization.DataWithPropertyList(plist, NSPropertyListFormat.Xml, out error);
			if (nsData == null)
				throw new Exception("Failed to serialize plist");

			return Encoding.UTF8.GetBytes(nsData.ToString());
		}

		public static void SerializePlist(BinaryWriter writer, NSDictionary payload)
		{
			new MessageData(1, payload).Serialize(writer);
		}

		public void Serialize(BinaryWriter writer)
		{
			writer.Write(TotalSize);
			writer.Write(Version);
			writer.Write((int)Message);
			writer.Write(Tag);
			writer.Write(Payload);
		}

		public static MessageData Deserialize(BinaryReader reader)
		{
			var payloadSize = reader.ReadInt32() - HeaderSize;
			return new MessageData(
				reader.ReadInt32(),
				(Message)reader.ReadInt32(),
				reader.ReadInt32(),
				reader.ReadBytes(payloadSize));
		}

		static int HeaderSize
		{
			get { return sizeof(int) + sizeof(int) + sizeof(int) + sizeof(Message); }
		}

		int TotalSize
		{
			get { return HeaderSize + Payload.Length; }
		}
	}

	static class BinaryStreamsExtensions
	{
		public static MessageData DeserializeMessage(this BinaryReader reader)
		{
			return MessageData.Deserialize(reader);
		}

		/// <exception cref="FailedToParseResponse"></exception>
		public static NSDictionary DeserializePlist(this BinaryReader reader)
		{
			try
			{
				return PlistSerializer.PayloadToNSDictionary(MessageData.Deserialize(reader).Payload);
			}
			catch (Exception)
			{
				throw new FailedToParseResponse();
			}
		}

		/// <exception cref="FailedToSendMessage"></exception>
		public static void SerializePlist(this BinaryWriter writer, NSDictionary payload)
		{
			try
			{
				MessageData.SerializePlist(writer, payload);
			}
			catch (Exception)
			{
				throw new FailedToSendMessage();
			}
		}
	}

	class PlistSerializer
	{
		public static NSDictionary CreatePlistMessage(string messageType, object[] otherKeys = null, object[] otherValues = null)
		{
			var k = new object[] { "MessageType" }.Union(otherKeys ?? new object[0]);
			var v = new object[] { messageType }.Union(otherValues ?? new object[0]);
			return NSDictionary.FromObjectsAndKeys(v.ToArray(), k.ToArray());
		}

		/// <exception cref="FailedToParsePlist"></exception>
		public static NSDictionary PayloadToNSDictionary(byte[] payload)
		{
			var plistData = NSData.FromArray(payload);
			var format = NSPropertyListFormat.Xml;

			NSError error;
			var ret = (NSDictionary)NSPropertyListSerialization.PropertyListWithData(plistData, ref format, out error);
			if (ret == null)
				throw new FailedToParsePlist();

			return ret;
		}
	}

	public static class UsbMux
	{
		/// <exception cref="FailedToParseResponse"></exception>
		/// <exception cref="FailedToConnectToUSBMux"></exception>
		/// <exception cref="FailedToSendMessage"></exception>
		public static Device[] ListDevices()
		{
			using (var stream = ConnectToUsbMux())
			{
				var reader = new BinaryReader(stream);
				var writer = new BinaryWriter(stream);

				writer.SerializePlist(PlistSerializer.CreatePlistMessage("ListDevices"));

				var listDevicesResult = reader.DeserializePlist();
				var deviceList = (NSArray)listDevicesResult["DeviceList"];
				var ret = new List<Device>();

				for (uint i = 0; i < deviceList.Count; ++i)
				{
					var device = new NSDictionary(deviceList.ValueAt(i));
					var deviceId = (NSNumber)device["DeviceID"];
					var properties = (NSMutableDictionary)device["Properties"];

					var connectionType = (NSString)properties["ConnectionType"];
					if (connectionType != "USB")
						continue;

					var productId = (NSNumber)properties["ProductID"];
					var serialNumber = (NSString)properties["SerialNumber"];
					var locationId = (NSNumber)properties["LocationID"];
					ret.Add(new Device(deviceId.UInt32Value, (ushort)productId, StringToByteArray(serialNumber.ToString()), (uint)locationId));
				}

				return ret.ToArray();
			}
		}

		static byte[] StringToByteArray(string hex)
		{
			return Enumerable.Range(0, hex.Length)
				.Where(x => x % 2 == 0)
				.Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
				.ToArray();
		}

		/// <exception cref="FailedToConnectToDevice"></exception>
		/// <exception cref="FailedToParseResponse"></exception>
		/// <exception cref="FailedToConnectToUSBMux"></exception>
		/// <exception cref="FailedToSendMessage"></exception>
		public static Stream Connect(Device device, short port)
		{
			var stream = ConnectToUsbMux();
			var reader = new BinaryReader(stream, Encoding.UTF8, true);
			var writer = new BinaryWriter(stream, Encoding.UTF8, true);

			writer.SerializePlist(
				PlistSerializer.CreatePlistMessage("Connect",
					new object[] { "DeviceID", "PortNumber" },
					new object[] { device.DeviceID, IPAddress.HostToNetworkOrder(port) }));

			var parsedPayload = reader.DeserializePlist();
			var resultCode = ((NSNumber)parsedPayload["Number"]).Int32Value;

			var response = (Result)resultCode;
			if (response != Result.Ok)
				throw new FailedToConnectToDevice("Expected response to be 'Ok', but was '" + response + "'", device);

			return stream;
		}

		/// <exception cref="FailedToConnectToUSBMux"></exception>
		static NetworkStream ConnectToUsbMux()
		{
			try
			{
				EndPoint endPoint = new UnixEndPoint("/var/run/usbmuxd");
				var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				socket.Connect(endPoint);
				return new NetworkStream(socket);
			}
			catch (Exception)
			{
				throw new FailedToConnectToUSBMux();
			}
		}
	}

	class MainClass
	{

		public static void Main(string[] args)
		{
			try
			{
				MonoMac.AppKit.NSApplication.Init();

				var devices = UsbMux.ListDevices();
				foreach (var d in devices)
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
			}
			catch (Exception e)
			{
				Console.WriteLine("fatal exception: {0}", e.ToString());
			}
		}
	}
}