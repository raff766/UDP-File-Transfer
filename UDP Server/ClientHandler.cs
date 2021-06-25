using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace ServerApp {
    class ClientHandler {
        private Thread processingThread;
        private Thread sendingThread;
        private IPEndPoint clientIP;
        private BlockingCollection<Packet> packetQueue = new BlockingCollection<Packet>();
        private ConcurrentDictionary<int, Packet> dataPacketsToSend = new ConcurrentDictionary<int, Packet>();
        private bool isConnected;

        public ClientHandler(IPEndPoint clientIP) {
            this.clientIP = clientIP;
            processingThread = new Thread(ProcessPackets);
            processingThread.Start();
            sendingThread = new Thread(SendDataPackets);
            sendingThread.Start();
        }

        public void HandlePacket(Packet packet) {
            //Console.WriteLine($"Received packet of type {packet.PacketType} with a size of {packet.Size} from {clientIP}");
            if(packet.PacketType == PacketType.Rec) {
                dataPacketsToSend.TryRemove(BinaryPrimitives.ReadInt32BigEndian(packet.Payload), out _);
            } else {
                packetQueue.Add(packet);
            }
        }

        private void SendDataPackets() {
            while(true) {
                SpinWait.SpinUntil(() => dataPacketsToSend.Count > 0);
                foreach(Packet packet in dataPacketsToSend.Values) {
                    SendPacket(packet);
                }
            }
        }

        private void ProcessPackets() {
            Packet receivedPacket;
            while(true) {
                if(packetQueue.TryTake(out receivedPacket, 5000)) {
                    if(receivedPacket.PacketType == PacketType.DCon) {
                        Disconnect();
                        break;
                    } else if(!isConnected && receivedPacket.PacketType == PacketType.Con) {
                        if(TryEstablishConnection()) {
                            isConnected = true;
                        } else {
                            Disconnect();
                            break;
                        }
                    } else if(receivedPacket.PacketType == PacketType.ReqI) {
                        List<FileInfo> files = new List<FileInfo>();
                        foreach (string file in Directory.GetFiles("Downloadable Files")) {
                            files.Add(new FileInfo(file));
                        }
                        
                        StringBuilder stringBuilder = new StringBuilder();
                        foreach(FileInfo file in files) {
                            stringBuilder.Append(file.Name + " - ");
                            stringBuilder.AppendLine(file.Length.ToString() + " Bytes");
                        }
                        Console.WriteLine("Sending info...");
                        SendDataWhole(Encoding.UTF8.GetBytes(stringBuilder.ToString()));
                    } else if(receivedPacket.PacketType == PacketType.ReqF) {
                        string fileName = Encoding.UTF8.GetString(receivedPacket.Payload);
                        using FileStream fileStream = new FileStream("Downloadable Files/" + fileName, FileMode.Open, FileAccess.Read);
                        SendDataStream(fileStream);
                    }
                }
            }
        }

        private bool TryEstablishConnection() {
            Packet receivedPacket;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            while(true) {
                SendPacket(new Packet(PacketType.Ack));
                if (packetQueue.TryTake(out receivedPacket, 10)) {
                    if (receivedPacket.PacketType == PacketType.ReqI) {
                        Console.WriteLine("Successfully connected to a new client!");
                        return true;
                    }
                }
                if (stopwatch.ElapsedMilliseconds >= 10000) {
                    Console.WriteLine("Connection Timed Out");
                    return false;
                }
            }
        }

        private void SendDataWhole(byte[] bytesToSend) {
            byte[] metaDataBytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(metaDataBytes.AsSpan(), bytesToSend.Length);
            dataPacketsToSend[-1] = new Packet(PacketType.MetaD, metaDataBytes);
            SpinWait.SpinUntil(() => dataPacketsToSend.ContainsKey(-1));
            Packet[] packets = Packet.GetDataPackets(bytesToSend);
            for(int i = 0; i < packets.Length; i++) {
                dataPacketsToSend[i] = packets[i];
            }
        }

        private void SendDataStream(FileStream fileStream) {
            int maxPayloadSize = Server.MaxPacketSize - 5;
            int defaultChunkSize = maxPayloadSize * 100;
            int chunkSize = defaultChunkSize < fileStream.Length ? defaultChunkSize : (int)fileStream.Length;
            Span<byte> fileDataChunk = new byte[chunkSize].AsSpan();
            int remainingBytesInChunk = Int32.MaxValue;
            int posInFileDataChunk = 0;
            int packetID = 0;
            bool hasCompletedRead = false;
            byte[] metaDataBytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(metaDataBytes.AsSpan(), (int)fileStream.Length);
            dataPacketsToSend[-1] = new Packet(PacketType.MetaD, metaDataBytes);
            SpinWait.SpinUntil(() => dataPacketsToSend.ContainsKey(-1));
            fileStream.Read(fileDataChunk);
            while(!hasCompletedRead) {
                if(posInFileDataChunk != fileDataChunk.Length) {
                    remainingBytesInChunk = (int)(fileDataChunk.Length - posInFileDataChunk);
                    int numOfBytesToPack = (remainingBytesInChunk < maxPayloadSize) ? remainingBytesInChunk : maxPayloadSize;
                    byte[] bytesToSend = new byte[numOfBytesToPack + 4];
                    Span<byte> bytesToSendSpan = bytesToSend.AsSpan();
                    BinaryPrimitives.WriteInt32BigEndian(bytesToSend.AsSpan(), packetID);
                    fileDataChunk.Slice(posInFileDataChunk, numOfBytesToPack).CopyTo(bytesToSend.AsSpan().Slice(4));
                    dataPacketsToSend[packetID] = new Packet(PacketType.Data, bytesToSend);
                    posInFileDataChunk += numOfBytesToPack;
                    packetID++;
                } else {
                    chunkSize = defaultChunkSize < (fileStream.Length - fileStream.Position) ? defaultChunkSize : (int)(fileStream.Length - fileStream.Position);
                    fileDataChunk = new byte[chunkSize].AsSpan();
                    posInFileDataChunk = 0;
                    if(fileStream.Read(fileDataChunk) == 0) {
                        hasCompletedRead = true;
                    }
                }
            }
        }
        
        private void Disconnect() {
            Server.ConnectedClients.Remove(clientIP);
        }

        private void SendPacket(Packet packet) {
            Server.UdpClient.Send(packet.GetBytes(), packet.Size, clientIP);
            //Console.WriteLine($"Sent packet of type {packet.PacketType} with a size of {packet.Size} bytes to {clientIP}");
        }
    }
}