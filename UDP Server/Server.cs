using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ServerApp {
    class Server {
        public const int Port = 9091;
        public const int MaxPacketSize = 508; //Practical maximum size is 508 bytes
        public static readonly Dictionary<IPEndPoint, ClientHandler> ConnectedClients = new Dictionary<IPEndPoint, ClientHandler>();
        public static readonly UdpClient UdpClient = new UdpClient(Port);

        static void Main(string[] args) {
            Console.WriteLine("Launching server...");
            IPEndPoint ep = null;
            ClientHandler clientHandler;
            UdpClient.Client.ReceiveBufferSize = 32784;
            UdpClient.Client.SendBufferSize = 32784;
            while(true) {
                byte[] inputBuffer = UdpClient.Receive(ref ep);
                Packet receivedPacket = new Packet(inputBuffer);
                //Console.WriteLine($"Recieving packet of type {receivedPacket.PacketType} with a size of {receivedPacket.Size} from {ep}");

                if(ConnectedClients.TryGetValue(ep, out clientHandler)) {
                    clientHandler.HandlePacket(receivedPacket);
                } else {
                    clientHandler = new ClientHandler(ep);
                    ConnectedClients.Add(ep, clientHandler);
                    clientHandler.HandlePacket(receivedPacket);
                }
            }
        }
    }
}
