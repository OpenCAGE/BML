// Alien Isolation (Binary XML converter)
// Written by WRS (xentax.com)

using System.Text;
using System.IO;

namespace CATHODE.BML
{
    public class AlienBML
    {
        // basic extension check
        static bool CheckExtension(string filename, string lower_ext = "bml")
        {
            int li = filename.LastIndexOf('.');
            if (li != -1) filename = filename.Substring(li + 1);
            return filename.ToLower() == lower_ext;
        }

        // switch between extensions
        static string ConvertFileName(string filename)
        {
            int li = filename.LastIndexOf('.');
            string fn = (li != -1) ? filename.Substring(0, li) : filename;
            if (CheckExtension(filename)) return fn + ".xml";
            else return fn + ".bml";
        }

        string filename_src, filename_dst;

        bool ConvertFile(ref BML bml)
        {
            bool result = true;

            if (File.Exists(filename_dst)) File.Delete(filename_dst);
            BinaryWriter bw = new BinaryWriter(File.OpenWrite(filename_dst));

            if (CheckExtension(filename_dst, "xml") )
            {
                string data = "";
                bml.ExportXML(ref data);

                // writing raw data otherwise we get the string encoding header
                bw.Write(Encoding.Default.GetBytes(data), 0, data.Length);
            }
            else if( CheckExtension(filename_dst) )
            {
                bml.ExportBML(bw);
            }
            else
            {
                result = false;
            }

            bw.Close();
            return result;
        }

        bool ProcessFile()
        {
            FileStream strm;

            try
            {
                strm = File.OpenRead(filename_src);
            }
            catch
            {
                return false;
            }

            if (!strm.CanRead) return false;
                
            BinaryReader br = new BinaryReader(strm);
            BML bml = null;
            bool valid = true;

            if (CheckExtension(filename_src))
            {
                bml = new BML();
                valid = bml.ReadBML(br);
            }
            else if (CheckExtension(filename_src, "xml"))
            {
                bml = new BML();
                valid = bml.ReadXML(br);
            }
            else
            {
                valid = false;
            }

            strm.Close();

            if(valid) return ConvertFile(ref bml);
            return valid;
        }

        public AlienBML(string filenameSource, string filenameDestination)
        {
            filename_src = filenameSource;
            filename_dst = filenameDestination;  
        }

        public bool Run()
        {
            if (filename_src == null) return true;
            else
            {
                if (filename_dst == null) filename_dst = ConvertFileName(filename_src);
                return ProcessFile();
            }
        }
    }
}
