using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace QuestPianoMotion.Research.Distributed
{
    public sealed class ReceivedDatagram
    {
        internal readonly byte[] Buffer; public readonly int Length; public readonly IPEndPoint Remote; public readonly double ReceiveTimestamp;
        public byte[] Data => Buffer;
        internal ReceivedDatagram(byte[] buffer,int length,IPEndPoint remote,double timestamp){Buffer=buffer;Length=length;Remote=remote;ReceiveTimestamp=timestamp;}
    }

    public sealed class UdpTransport : IDisposable
    {
        readonly ConcurrentQueue<ReceivedDatagram> m_Received=new ConcurrentQueue<ReceivedDatagram>();
        readonly ConcurrentBag<byte[]> m_Pool=new ConcurrentBag<byte[]>(); readonly int m_QueueCapacity;
        Socket m_Socket; Thread m_Thread; volatile bool m_Running; int m_QueueCount; long m_Dropped,m_SendFailures,m_ReceiveFailures;
        public int LocalPort { get; private set; } public int QueueDepth=>Volatile.Read(ref m_QueueCount);
        public long Dropped=>Interlocked.Read(ref m_Dropped); public long SendFailures=>Interlocked.Read(ref m_SendFailures); public long ReceiveFailures=>Interlocked.Read(ref m_ReceiveFailures);
        public UdpTransport(int queueCapacity=256){m_QueueCapacity=Math.Max(8,queueCapacity);}
        public void Start(int localPort)
        {
            if(m_Running)return; m_Socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
            m_Socket.ReceiveTimeout=500;m_Socket.SendBufferSize=256*1024;m_Socket.ReceiveBufferSize=1024*1024;m_Socket.Bind(new IPEndPoint(IPAddress.Any,localPort));
            LocalPort=((IPEndPoint)m_Socket.LocalEndPoint).Port;m_Running=true;m_Thread=new Thread(ReceiveLoop){IsBackground=true,Name="QuestPiano UDP receive"};m_Thread.Start();
        }
        public bool Send(byte[] buffer,int length,IPEndPoint remote)
        {
            var socket=m_Socket;if(!m_Running||socket==null||remote==null)return false;
            try{return socket.SendTo(buffer,0,length,SocketFlags.None,remote)==length;}catch(SocketException){Interlocked.Increment(ref m_SendFailures);return false;}catch(ObjectDisposedException){return false;}
        }
        public bool TryDequeue(out ReceivedDatagram datagram)
        {if(!m_Received.TryDequeue(out datagram))return false;Interlocked.Decrement(ref m_QueueCount);return true;}
        public void Recycle(ReceivedDatagram datagram){if(datagram?.Buffer!=null)m_Pool.Add(datagram.Buffer);}
        void ReceiveLoop()
        {
            var receiveBuffer=new byte[NetworkProtocolV1.MaximumDatagramBytes+1]; EndPoint remote=new IPEndPoint(IPAddress.Any,0);
            while(m_Running)
            {
                try
                {
                    var count=m_Socket.ReceiveFrom(receiveBuffer,0,receiveBuffer.Length,SocketFlags.None,ref remote);if(count>NetworkProtocolV1.MaximumDatagramBytes){Interlocked.Increment(ref m_Dropped);continue;}
                    if(Interlocked.Increment(ref m_QueueCount)>m_QueueCapacity){Interlocked.Decrement(ref m_QueueCount);Interlocked.Increment(ref m_Dropped);continue;}
                    if(!m_Pool.TryTake(out var copy))copy=new byte[NetworkProtocolV1.MaximumDatagramBytes];Buffer.BlockCopy(receiveBuffer,0,copy,0,count);
                    m_Received.Enqueue(new ReceivedDatagram(copy,count,(IPEndPoint)remote,ResearchServices.Clock.AbsoluteSeconds));remote=new IPEndPoint(IPAddress.Any,0);
                }
                catch(SocketException e){if(m_Running&&e.SocketErrorCode!=SocketError.TimedOut)Interlocked.Increment(ref m_ReceiveFailures);}
                catch(ObjectDisposedException){break;}catch(Exception){if(m_Running)Interlocked.Increment(ref m_ReceiveFailures);}
            }
        }
        public void Stop(){m_Running=false;try{m_Socket?.Close();}catch(Exception){}if(m_Thread!=null&&m_Thread.IsAlive)m_Thread.Join(1000);m_Thread=null;m_Socket=null;while(TryDequeue(out var d))Recycle(d);}
        public void Dispose()=>Stop();
    }
}
