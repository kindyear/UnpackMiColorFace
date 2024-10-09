using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnpackMiColorFace.Helpers;

namespace UnpackMiColorFace
{
    internal class Program
    {
        public const string library = "UnpackMiColorFace.Lib.Magick.NET-Q16-AnyCPU.dll";
        public const string libraryCommon = "UnpackMiColorFace.Lib.XiaomiWatch.Common.dll";

        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver;
            MainBody(args);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MainBody(string[] args)
        {
            if (args.Count() == 0)
            {
                Console.WriteLine("usage: UnpackMiColorFace example.bin");
                return;
            }

            var filename = args[0];

            if (!File.Exists(filename))
            {
                Console.WriteLine($"File {filename} is not found.");
                return;
            }

            try
            {
                Unpacker.Exec(filename);

                if (LogHelper.GotError)
                    Console.ReadKey();
            }
            catch (MissingFieldException)
            {
                Console.WriteLine("Seems wrong file passed,\r\nPlease check a source file is correct Watchface");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine("Got unexcepted error,\r\nPlease check a source file is correct Watchface");
                Console.ReadKey();
            }
        }

        private static Assembly AssemblyResolver(object sender, ResolveEventArgs args)
        {
            Assembly asm = null;

            asm = LoadAssembly(args, "Magick", library);
            if (asm != null) return asm;

            asm = LoadAssembly(args, "XiaomiWatch", libraryCommon);
            if (asm != null) return asm;

            return asm;
        }

        private static Assembly LoadAssembly(ResolveEventArgs args, string name, string libName)
        {
            var assembly = Assembly.GetExecutingAssembly();

            if (args.Name.Contains(name))
                using (var stream = assembly.GetManifestResourceStream(libName))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        var rawAssembly = new byte[stream.Length];
                        reader.Read(rawAssembly, 0, rawAssembly.Length);
                        var asm = Assembly.Load(rawAssembly);
                        return asm;
                    }
                }

            return null;
        }
    }
}