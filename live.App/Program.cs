using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using live.Utils;
using Newtonsoft.Json;

namespace live.App
{
    class Program
    {
        static void Main(string[] args)
        {
            // string ip = "127.0.0.1";
            // int port = 12345;
            Console.WriteLine("请输入BiliBili直播房间号: ");
            int roomID = int.Parse(Console.ReadLine());
            LiveHelper helper = new LiveHelper(roomID);
            helper.ReceiveGift += (username, giftname, count) =>
            {
                Console.WriteLine(username + " : " + giftname + "*" + count);
                // var obj = new
                // {
                //     name = giftname,
                //     count = count,
                //     user = username
                // };
                // var json = JsonConvert.SerializeObject(obj);

                // try
                // {
                //     SocketHelper.SendTcp(Encoding.UTF8.GetBytes(json), new IPEndPoint(IPAddress.Parse(ip), port));
                // }
                // catch (Exception)
                // {
                //     Console.WriteLine("异常: " + ex.Message);
                // }
            };
            helper.ReceiveDanmu += (username, msg) =>
            {
                Console.WriteLine(username + " : " + msg);
            };
            helper.StartReceive();
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey();

            } while (key.Key != ConsoleKey.Q);
            Console.WriteLine("Cloesing...");
        }
    }
}
