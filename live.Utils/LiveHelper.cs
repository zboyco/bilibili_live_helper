using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace live.Utils
{
    public class LiveHelper
    {
        TcpClient Client;
        string host;
        int port;
        NetworkStream NetStream;
        bool Connected;
        bool debuglog = false;

        string RoomInfoUrl = "https://api.live.bilibili.com/room/v1/Room/room_init?id=";
        string CIDInfoUrl = "http://live.bilibili.com/api/player?id=cid:";

        public Action<string, string, int> ReceiveGift;
        public Action<string, string> ReceiveDanmu;
        public Action<int> ReceivePopularValue;

        public LiveHelper(int roomID)
        {
            try
            {
                var request = new HttpClient();
                {
                    var text = request.GetStringAsync(RoomInfoUrl + roomID);
                    var json = JObject.Parse(text.Result);
                    if (json["code"].ToString() != "0")
                    {
                        throw new Exception("房间号错误!");
                    }
                    roomID = int.Parse(json["data"]["room_id"].ToString());
                }
                {
                    var text = request.GetStringAsync(CIDInfoUrl + roomID);
                    XmlDocument doc = new XmlDocument();
                    var xml = "<root>" + text.Result + "</root>";
                    doc.LoadXml(xml);
                    this.host = doc["root"]["dm_server"].InnerText;
                    this.port = int.Parse(doc["root"]["dm_port"].InnerText);
                    Connect(roomID);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        void Connect(int roomID)
        {
            try
            {
                Client = new TcpClient();
                Client.Connect(host, port);
                NetStream = Client.GetStream();
                if (EnterRoom(roomID))
                {
                    Connected = true;
                    this.HeartbeatLoop();
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public void StartReceive()
        {
            var thread = new Thread(this.ReceiveMessageLoop)
            {
                IsBackground = true
            };
            thread.Start();
        }

        async void HeartbeatLoop()
        {

            try
            {
                while (this.Connected)
                {
                    this.SendHeartbeat();
                    await Task.Delay(30000);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        void SendHeartbeat()
        {
            SendSocketData(2);
        }

        void SendSocketData(int action, string body = "")
        {
            SendSocketData(0, 16, 1, action, 1, body);
        }
        void SendSocketData(int packetlength, short magic, short ver, int action, int param = 1, string body = "")
        {
            var playload = Encoding.UTF8.GetBytes(body);
            if (packetlength == 0)
            {
                packetlength = playload.Length + 16;
            }
            var buffer = new byte[packetlength];
            using (var ms = new MemoryStream(buffer))
            {

                var b = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(buffer.Length));
                ms.Write(b, 0, 4);
                b = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(magic));
                ms.Write(b, 0, 2);
                b = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(ver));
                ms.Write(b, 0, 2);
                b = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(action));
                ms.Write(b, 0, 4);
                b = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(param));
                ms.Write(b, 0, 4);
                if (playload.Length > 0)
                {
                    ms.Write(playload, 0, playload.Length);
                }
                NetStream.Write(buffer, 0, buffer.Length);
                NetStream.Flush();
            }
        }

        // 加入
        bool EnterRoom(int roomID)
        {
            Random r = new Random();
            var tmpuid = r.Next(int.MaxValue);//(long)(1e14 + 2e14 * r.NextDouble());
            var packetModel = new { roomid = roomID, uid = tmpuid };
            var playload = JsonConvert.SerializeObject(packetModel);
            SendSocketData(7, playload);
            return true;
        }

        void ReceiveMessageLoop()
        {
            try
            {
                var buffer = new byte[4];

                while (this.Connected)
                {
                    NetStream.Read(buffer, 0, 4);
                    var packetlength = BitConverter.ToInt32(buffer, 0);
                    packetlength = IPAddress.NetworkToHostOrder(packetlength);

                    if (packetlength < 16)
                    {
                        throw new NotSupportedException("协议失败: (L:" + packetlength + ")");
                    }

                    NetStream.Read(buffer, 0, 2);//magic
                    NetStream.Read(buffer, 0, 2);//protocol_version 

                    NetStream.Read(buffer, 0, 4);
                    var typeId = BitConverter.ToInt32(buffer, 0);
                    typeId = IPAddress.NetworkToHostOrder(typeId);


                    //Console.WriteLine(typeId);
                    NetStream.Read(buffer, 0, 4);//magic, params?
                    var playloadlength = packetlength - 16;
                    if (playloadlength == 0)
                    {
                        continue;//没有内容了

                    }
                    var palyloadBuffer = new byte[playloadlength];
                    NetStream.Read(palyloadBuffer, 0, playloadlength);
                    // Console.WriteLine(typeId);
                    switch (typeId)
                    {
                        case 3:
                            {
                                var viewer = BitConverter.ToInt32(palyloadBuffer, 0); //观众人数
                                viewer = IPAddress.NetworkToHostOrder(viewer);
                                if (debuglog)
                                {
                                    Console.WriteLine("人气值 : " + viewer);
                                }
                                ReceivePopularValue?.Invoke(viewer);
                                break;
                            }
                        case 5://playerCommand
                            {
                                var json = Encoding.UTF8.GetString(palyloadBuffer, 0, playloadlength);
                                if (debuglog)
                                {
                                    Console.WriteLine(json);
                                }
                                MsgFilter(json);
                                break;
                            }
                        default:
                            {
                                break;
                            }
                    }
                }
            }
            catch (NotSupportedException ex)
            {
                throw ex;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        void MsgFilter(string json)
        {
            JObject obj = null;
            try
            {
                obj = JObject.Parse(json);
            }
            catch
            {
                Console.WriteLine("error json");
                // 不能转为json直接忽略
                return;
            }


            string cmd = obj["cmd"].ToString();
            switch (cmd)
            {
                case "DANMU_MSG": // 弹幕
                    {
                        var commentText = obj["info"][1].ToString();
                        var userName = obj["info"][2][1].ToString();
                        ReceiveDanmu?.Invoke(userName, commentText);
                    }
                    break;
                case "SEND_GIFT": // 礼物
                    {
                        var giftName = obj["data"]["giftName"].ToString();
                        var userName = obj["data"]["uname"].ToString();
                        var giftCount = obj["data"]["num"].ToObject<int>();
                        ReceiveGift?.Invoke(userName, giftName, giftCount);
                    }
                    break;
                default:
                    break;
            }
        }
    }
}