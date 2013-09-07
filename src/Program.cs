using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Kinect;
using ZMQ;

namespace kinect_server
{
    class Program
    {
        private static Context context;
        private static Socket socket;
        private static Socket depthSocket;
        private static Socket imgSocket;
        private static Socket elevationSocket;

        private static byte[] pixelData;
        private static Skeleton[] skeletonData;
        private static short[] depthData;
        private static KinectSensor sensor;
        static void Main(string[] args)
        {
            sensor = KinectSensor.KinectSensors[0];
            if (sensor.Status == KinectStatus.Connected)
            {
                Console.WriteLine("Connected...");
                sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                sensor.ColorFrameReady += new EventHandler<ColorImageFrameReadyEventArgs>(sensor_ColorFrameReady);
                sensor.SkeletonStream.Enable();
                sensor.SkeletonFrameReady += new EventHandler<SkeletonFrameReadyEventArgs>(sensor_SkeletonFrameReady);
                sensor.DepthStream.Enable();
                sensor.DepthFrameReady += new EventHandler<DepthImageFrameReadyEventArgs>(sensor_DepthFrameReady);
                
                context = new Context(1);
                socket = context.Socket(SocketType.PUB);
                socket.Bind("tcp://*:20001");
                depthSocket = context.Socket(SocketType.PUB);
                depthSocket.Bind("tcp://*:20002");
                imgSocket = context.Socket(SocketType.PUB);
                imgSocket.Bind("tcp://*:20003");
                elevationSocket = context.Socket(SocketType.REP);
                elevationSocket.Bind("tcp://*:20004");
                var items = new PollItem[1];
                items[0] = elevationSocket.CreatePollItem(IOMultiPlex.POLLIN);
                items[0].PollInHandler += new PollHandler(Program_PollInHandler);
                sensor.Start();
                bool interrupted = false;
                Console.CancelKeyPress += delegate { interrupted = true; };
                while (!interrupted)
                {
                    context.Poll(items, -1);
                }
            }
        }

        static void Program_PollInHandler(Socket elev_socket, IOMultiPlex revents)
        {
            var msg = elev_socket.Recv();
            string angle = Encoding.UTF8.GetString(msg);
            int elevation;
            if (int.TryParse(angle, out elevation))
            {
                if (elevation < sensor.MaxElevationAngle && elevation > sensor.MinElevationAngle)
                {
                    sensor.ElevationAngle = elevation;
                }
            }
            elev_socket.Send(Convert.ToString(sensor.ElevationAngle), Encoding.UTF8);
        }

        static void sensor_DepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (var depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame != null)
                {
                    depthData = new short[depthFrame.PixelDataLength];
                }
                depthFrame.CopyPixelDataTo(depthData);
                var bytes = depthData.Select(b => BitConverter.GetBytes(b)).SelectMany(a => a).ToArray();
                depthSocket.SendMore("depth", Encoding.UTF8);
                depthSocket.Send(bytes);
            }
        }


        static void sensor_SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    if (skeletonData == null)
                    {
                        skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    }
                    skeletonFrame.CopySkeletonDataTo(skeletonData);
                    List<Skeleton> skeletons = new List<Skeleton>();
                    foreach (var user in skeletonData)
                    {
                        if (user.TrackingState == SkeletonTrackingState.Tracked)
                        {
                            skeletons.Add(user);
                        }
                    }
                    if (skeletons.Count > 0)
                    {
                        socket.SendMore("skeleton", Encoding.UTF8);
                        socket.Send(skeletons.Serialize(), Encoding.UTF8);
                    }
                }
            }            
        }

        static void sensor_ColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            using (ColorImageFrame imageFrame = e.OpenColorImageFrame())
            {
                if (imageFrame != null)
                {
                    if (pixelData == null)
                    {
                        pixelData = new byte[imageFrame.PixelDataLength];
                    }
                    imageFrame.CopyPixelDataTo(pixelData);
                    imgSocket.SendMore("image", Encoding.UTF8);
                    imgSocket.Send(pixelData);                    
                }
            }
        }
    }
}
