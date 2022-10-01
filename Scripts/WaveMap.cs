using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WFC{
    public class WaveRaw
    {
        public int[] data;
        public static WaveRaw CreateFromJSON(string jsonString)
        {
            return JsonUtility.FromJson<WaveRaw>(jsonString);
        }
        public string SaveToString()
        {
            return JsonUtility.ToJson(this);
        }
    }

    public class WaveMap
    {
        public int[] raw_data;
        public int[,,] data;

        public int width;
        public int height;

        public WaveMap(int width, int height)
        {
            this.width = width;
            this.height = height;
            raw_data = new int[width * height];
            data = new int[width, height, 2];
        }

        public void decode(int[]raw_data)
        {
            for (int i = 0; i < width * height; i++)
            {
                this.raw_data[i] = raw_data[i];
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    data[x, y, 0] = raw_data[x*2 + y * width * 2 + 0];
                    data[x, y, 1] = raw_data[x*2 + y * width * 2 + 1];
                }
            }
        }
        public void decode(Google.Protobuf.Collections.RepeatedField<int> raw_data)
        {
            for (int i = 0; i < width * height; i++)
            {
                this.raw_data[i] = raw_data[i];
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    data[x, y, 0] = raw_data[x*2 + y * width * 2 + 0];
                    data[x, y, 1] = raw_data[x*2 + y * width * 2 + 1];
                }
            }
        }

        public void show()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ////Debug.Log(data[x, y, 0] + " " + data[x, y, 1]);
                }
            }
        }

    }
}