using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace XMLPreprocessor
{
    class Program
    {
        static int Main(string[] args)
        {
            string inXml;
            string outXml = "";
            if (args.Length > 0)
            {
                inXml = args[0];
                if (args.Length == 2)
                {
                    outXml = args[1];
                }
            }
            else
            {
                Console.WriteLine("XMLPreprocessor.exe in.xml [out.xml]");
                return -1;
            }
            
            
            Preprocessor preprocessor = new Preprocessor();
            XmlDocument processedXmlDoc = new XmlDocument();
            processedXmlDoc = preprocessor.Process(inXml, null);
            if(outXml == "")
            {
                processedXmlDoc.Save(inXml);
            }
            else
            {
                processedXmlDoc.Save(outXml);
            }
            return 0;
        }
    }
}
