using Rug.Osc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Diagnostics;
using Newtonsoft.Json;
using System.IO;

namespace VMC2WS
{
    internal class VMC2WS
    {
        static OscReceiver receiver;
        static Thread thread;

        static WebSocketServer server;

        static WebSocket ws;

        static long SendCount = 0;

        static string Mode = "Relay";

        static string RecordFilePath = "Record.txt";

        static void Main(string[] args)
        {
            //Read settings
            var filePath = "config.json";
            if (File.Exists(filePath))
            {
                dynamic jsonObj = JsonConvert.DeserializeObject(File.ReadAllText(filePath));
                Mode = jsonObj["Mode"];
            }

            Console.WriteLine("Mode = " + Mode);

            //Create Websocket server
            server = new WebSocketServer(3000);
            server.AddWebSocketService<Echo>("/");
            server.Start();

            //Create Websocket client
            ws = new WebSocket("ws://localhost:3000/");

            ws.OnOpen += (sender, e) =>
            {
                Console.WriteLine("WebSocket Open");
            };

            ws.OnMessage += (sender, e) =>
            {
                //Console.WriteLine("WebSocket Message Type: " + ", Data: " + e.Data);
                SendCount++;
                Console.SetCursorPosition(0, Console.CursorTop - 1);
                Console.WriteLine("送信中:" + SendCount);
            };

            ws.OnError += (sender, e) =>
            {
                Console.WriteLine("WebSocket Error Message: " + e.Message);
            };

            ws.OnClose += (sender, e) =>
            {
                Console.WriteLine("WebSocket Close");
            };

            ws.Connect();

            if(Mode == "Relay" || Mode == "Record")
            {
                // Create osc receiver
                // This is the port we are going to listen on 
                int port = 39539;

                // Create the receiver
                receiver = new OscReceiver(port);

                // Create a thread to do the listening
                thread = new Thread(new ThreadStart(ListenLoop));

                // Connect the receiver
                receiver.Connect();

                // Start the listen thread
                thread.Start();

                // wait for a key press to exit
                Console.WriteLine("Press any key to exit");
                Console.WriteLine("");
                Console.ReadKey(true);

                // close the Reciver 
                receiver.Close();

                // Wait for the listen thread to exit
                thread.Join();
            }else if(Mode == "Play")
            {
                PlayRecord();
                Console.ReadLine();
            }

        }

        static void ListenLoop()
        {
            try
            {
                while (receiver.State != OscSocketState.Closed)
                {
                    // if we are in a state to recieve
                    if (receiver.State == OscSocketState.Connected)
                    {
                        // get the next message 
                        // this will block until one arrives or the socket is closed
                        OscPacket packet = receiver.Receive();

                        // Write the packet to the console 
                        //Console.WriteLine(packet.ToString());

                        // DO SOMETHING HERE!
                        
                        var NeosStrings = ParseForNeos(packet);
                        if(NeosStrings.Count > 0)
                        {
                            var NeosString = "$#" + String.Join("$#", NeosStrings.ToArray()) + "$#";
                            ws.Send(NeosString);

                            if(Mode == "Record")
                            {
                                RecordString(NeosString);
                            }
                        }
                        
                    }
                }
            }
            catch (Exception ex)
            {
                // if the socket was connected when this happens
                // then tell the user
                if (receiver.State == OscSocketState.Connected)
                {
                    Console.WriteLine("Exception in listen loop");
                    Console.WriteLine(ex.Message);
                }
            }
        }

        public class Echo : WebSocketBehavior
        {
            protected override void OnMessage(MessageEventArgs e)
            {
                
                Sessions.Broadcast(e.Data);
            }
        }


        public static List<string> ParseForNeos(OscPacket osc)
        {
            var packet = osc.ToString();
            var packets = packet.Split(',');
            //Console.WriteLine(String.Join("\n", packets.ToArray()));

            var NeosStrings = new List<string>();

            for(int i = 0; i < packets.Length; i++)
            {
                var t = packets[i];
                if(t.Contains("/VMC/Ext/Bone/Pos"))
                {
                    for(int j = 0; j < 8; j++)
                    {
                        NeosStrings.Add(packets[i + j + 1].Replace(" ","").Replace("\"","").Replace("{","").Replace("}",""));
                    }
                    
                }
            }

            for(int i = 0; i < NeosStrings.Count; i++)
            {
                if (NeosStrings[i].EndsWith("f"))
                {
                    NeosStrings[i] = NeosStrings[i].Remove(NeosStrings[i].Length - 1);
                }
            }

            /*
            if(NeosStrings.Count > 0)
            {
                Console.WriteLine(String.Join("$#", NeosStrings.ToArray()) + "$#");
            }
            */

            return NeosStrings;
        }

        public static void RecordString(string str)
        {
            var RecordText = "," + str;
            File.AppendAllText(RecordFilePath, RecordText);
        }

        static async void PlayRecord()
        {
            StreamReader streamReader = new StreamReader(RecordFilePath);
            string record = streamReader.ReadToEnd();
            var records = record.Split(',');

            for(int i = 0; i < records.Length; i++)
            {
                var send = records[i];
                ws.Send(send);

                if(i % 8 == 0)
                {
                    await Task.Delay(1);
                }
            }

            PlayRecord();
        }
    }
}
