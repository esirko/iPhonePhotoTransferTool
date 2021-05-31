using System;
using System.Linq;
using MediaDevices;

namespace iPhonePhotoTransferTool
{
    class Program
    {
        static void Main(string[] args)
        {
            var devices = MediaDevice.GetDevices();

            using (var device = devices.First(d => d.FriendlyName == "Apple iPhone"))
            {
                device.Connect();

                var mdi = device.GetDirectoryInfo("Internal Storage");

                foreach (MediaDirectoryInfo di in device.GetDirectoryInfo("Internal Storage/DCIM").EnumerateDirectories().OrderBy(d => d.Name))
                {
                    foreach (MediaFileInfo fi in di.EnumerateFiles().OrderBy(f => f.Name))
                    {
                        Console.WriteLine(fi.Name + " " + fi.Length);

                    }
                }

                /*
                device.CreateDirectory(@"\Phone\Documents\Temp");
                using (FileStream stream = File.OpenRead(@"C:/Temp/Test.txt"))
                {
                    device.UploadFile(stream, @"\Phone\Documents\Temp\Test.txt");
                }
                */
                device.Disconnect();
            }


        }
    }
}
