using System;
using System.Buffers.Binary;

namespace ServerApp {
    class Packet {
        public PacketType PacketType {get;}
        public byte[] Payload {get;}
        public int Size {get {return 1 + Payload.Length;}}

        public Packet(byte[] recievedBytes) {
            PacketType = (PacketType)recievedBytes[0];
            Payload = recievedBytes[1..];
        }

        public Packet(PacketType packetType, byte[] bytesToSend) {
            this.PacketType = packetType;
            Payload = bytesToSend;
        }

        public Packet(PacketType packetType) {
            this.PacketType = packetType;
            Payload = new byte[0];
        }

        public byte[] GetBytes() {
            byte[] bytes = new byte[Payload.Length + 1];
            bytes[0] = (byte)PacketType;
            Payload.CopyTo(bytes, 1);
            return bytes;
        }

        public static Packet[] GetDataPackets(byte[] bytesToConvert) {
            //Minus MaxPacketSize by 1 to compensate for PacketType and by another 4 to compensate for package id
            int maxPayloadSize = Server.MaxPacketSize - 5;
            int numOfPackets = (int) Math.Ceiling((decimal)bytesToConvert.Length / (decimal)maxPayloadSize);
            Packet[] packets = new Packet[numOfPackets];
            for (int i = 0; i < packets.Length; i++) {
                int pointer = i * maxPayloadSize;
                int remainingBytes = bytesToConvert.Length - pointer;
                int numOfBytesToPack = (remainingBytes < maxPayloadSize) ? remainingBytes : maxPayloadSize;
                byte[] bytesToSend = new byte[numOfBytesToPack + 4];
                Span<byte> payloadData = bytesToConvert.AsSpan(pointer, numOfBytesToPack);
                Span<byte> bytesToSendSpan = bytesToSend.AsSpan();
                BinaryPrimitives.WriteInt32BigEndian(bytesToSendSpan, i);
                payloadData.CopyTo(bytesToSendSpan.Slice(4));
                packets[i] = new Packet(PacketType.Data, bytesToSend);
            }
            
            return packets;
        }
    }

    public enum PacketType {
        Con, //Connection Packet
        ReqF, //Request File Packet
        ReqI, //Request Info Packet
        Ack, //Acknowledgment Packet
        Data, //Data Packet
        MetaD, //Metadata Packet
        Rec, //Reception Packet
        DCon //Disconnection Packet
    }
}