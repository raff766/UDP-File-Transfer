using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ClientApp {
    class Client {
        public const int Port = 9091;
        public const int MaxPacketSize = 508; //Practical maximum size is 508 bytes
        public static string IpAddress;
        private static bool isConnected;
        private static bool hasRecievedOptions;
        private static string downloadableOptions;
        private static UdpClient udpClient;
        private static BlockingCollection<Packet> packetQueue = new BlockingCollection<Packet>();

        static void Main(string[] args) {
            Console.WriteLine("Launching client...");
            Console.WriteLine("Type in the host server's IP Adress: ");
            IpAddress = Console.ReadLine();
            udpClient = new UdpClient(IpAddress, Port);
            udpClient.Client.ReceiveBufferSize = 32784;
            udpClient.Client.SendBufferSize = 32784;
            Thread inputThread = new Thread(ReceivePackets);
            inputThread.Priority = ThreadPriority.AboveNormal;
            inputThread.Start();
            TryEstablishConnection();
            while(isConnected) {
                if (!hasRecievedOptions) {
                    downloadableOptions = Encoding.UTF8.GetString(RequestInfo());
                    hasRecievedOptions = true;
                }
                Console.WriteLine("Input the name of the file that you would like to download.");
                Console.WriteLine(downloadableOptions);
                string fileName = Console.ReadLine();
                Stopwatch stopwatch = Stopwatch.StartNew();
                RequestFile(fileName);
                Console.WriteLine($"Downloaded file: {fileName}. Elapsed time was {(double)stopwatch.ElapsedMilliseconds / (double)1000} seconds.");
            }
            udpClient.Close();
            Console.WriteLine("Disconnected");
        }

        private static void ReceivePackets() {
            IPEndPoint ep = null;
            byte[] packetIDAsBytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(packetIDAsBytes, -1);
            Packet metadataReceptionPacket = new Packet(PacketType.Rec, packetIDAsBytes);
            while(true) {
                byte[] inputBuffer = udpClient.Receive(ref ep);
                Packet receivedPacket = new Packet(inputBuffer);
                //Console.WriteLine($"Received packet of type {receivedPacket.PacketType} with a size of {receivedPacket.Size} from {ep}");
                packetQueue.Add(receivedPacket);
                if (receivedPacket.PacketType == PacketType.Data) {
                    Packet dataReceptionPacket = new Packet(PacketType.Rec, receivedPacket.Payload[..4]);
                    //Console.WriteLine($"PacketID: {BinaryPrimitives.ReadInt32BigEndian(dataReceptionPacket.Payload.AsSpan())}");
                    SendPacket(dataReceptionPacket);
                } else if (receivedPacket.PacketType == PacketType.MetaD) {
                    SendPacket(metadataReceptionPacket);
                }
            }
        }

        private static void TryEstablishConnection() {
            Packet receivedPacket;
            Packet connectionPacket = new Packet(PacketType.Con);
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            while(true) {
                SendPacket(connectionPacket);
                if (packetQueue.TryTake(out receivedPacket, 10)) {
                    if (receivedPacket.PacketType == PacketType.Ack) {
                        isConnected = true;
                        Console.WriteLine("Successfully connected to the server!");
                        return;
                    }
                }
                if (stopwatch.ElapsedMilliseconds >= 10000) {
                    Console.WriteLine("Connection Timed Out");
                    return;
                }
            }
        }

        private static byte[] RequestInfo() {
            Packet receivedPacket;
            bool hasSeverReceivedRequest = false;
            Packet requestPacket = new Packet(PacketType.ReqI);
            SortedDictionary<int, Packet> recievedPackets = new SortedDictionary<int, Packet>();
            int amountToBeReceived = -1;
            int amountReceived = 0;
            while(packetQueue.TryTake(out _)){}
            while(amountReceived != amountToBeReceived) {
                if(!hasSeverReceivedRequest) {
                    SendPacket(requestPacket);
                }
                if (packetQueue.TryTake(out receivedPacket, 100)) {
                    if (receivedPacket.PacketType == PacketType.Data) {
                        int packetID = BinaryPrimitives.ReadInt32BigEndian(receivedPacket.Payload[..4].AsSpan());
                        if (!recievedPackets.ContainsKey(packetID)) {
                            recievedPackets.Add(packetID, receivedPacket);
                            amountReceived += receivedPacket.Size - 5;
                        }
                    } else if (receivedPacket.PacketType == PacketType.MetaD) {
                        hasSeverReceivedRequest = true;
                        amountToBeReceived = BinaryPrimitives.ReadInt32BigEndian(receivedPacket.Payload.AsSpan());
                    }
                }
            }
            List<byte> receivedData = new List<byte>();
            foreach (Packet packet in recievedPackets.Values) {
                receivedData.AddRange(packet.Payload[4..]);
            }
            return receivedData.ToArray();
        }

        private static void RequestFile(string fileName) {
            if (!downloadableOptions.Contains(fileName)) {
                Console.WriteLine("That file does not exist!");
                return;
            }
            using FileStream newFile = new FileStream("Downloaded Files/" + fileName, FileMode.Create);
            Packet receivedPacket;
            bool hasSeverReceivedRequest = false;
            Packet requestPacket = new Packet(PacketType.ReqF, Encoding.UTF8.GetBytes(fileName));
            SortedSet<int> recievedPackets = new SortedSet<int>();
            int amountToBeReceived = -1;
            int amountReceived = 0;
            while(packetQueue.TryTake(out _)){}
            while(amountReceived != amountToBeReceived) {
                if(!hasSeverReceivedRequest) {
                    SendPacket(requestPacket);
                }
                if (packetQueue.TryTake(out receivedPacket, 100)) {
                    if (receivedPacket.PacketType == PacketType.Data) {
                        int packetID = BinaryPrimitives.ReadInt32BigEndian(receivedPacket.Payload[..4].AsSpan());
                        if (!recievedPackets.Contains(packetID)) {
                            recievedPackets.Add(packetID);
                            amountReceived += receivedPacket.Size - 5;
                            newFile.Seek(packetID * (Client.MaxPacketSize - 5), SeekOrigin.Begin);
                            newFile.Write(receivedPacket.Payload[4..], 0, receivedPacket.Payload[4..].Length);
                        }
                    } else if (receivedPacket.PacketType == PacketType.MetaD) {
                        hasSeverReceivedRequest = true;
                        amountToBeReceived = BinaryPrimitives.ReadInt32BigEndian(receivedPacket.Payload.AsSpan());
                    }
                }
            }
            newFile.Flush();
        }

        private static void SendPacket(Packet packet) {
            udpClient.Send(packet.GetBytes(), packet.Size);
            //Console.WriteLine($"Sent packet of type {packet.PacketType} with a size of {packet.Size} bytes to {IpAddress + ":" + Port}");
        }
    }
}
