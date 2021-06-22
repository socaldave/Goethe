using Firesplash.UnityAssets.SocketIO.MIT;

namespace Firesplash.UnityAssets.SocketIO.MIT
{
    public class Parser {
        internal static SIOEventStructure Parse(string json) {
            string[] data = json.Split(new char[] { ',' }, 2);
            string eventName = data[0].Substring(2, data[0].Length - 3);

            if(data.Length == 1) {
                return new SIOEventStructure()
                {
                    eventName = eventName,
                    data = null
                };
            }

            return new SIOEventStructure()
            {
                eventName = eventName,
                data = data[1].TrimEnd(']')
            };
        }

        public string ParseData(string json) {
            return json.Substring(1, json.Length - 2);
        }

    }
}