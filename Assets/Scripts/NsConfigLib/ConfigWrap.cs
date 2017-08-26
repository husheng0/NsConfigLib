﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Utils;

// 配置文件库
namespace NsLib.Config {
    
    // 转换器
    public static class ConfigWrap {

        public static Dictionary<K, V> ToObject<K, V>(byte[] buffer, bool isLoadAll = false) where V: ConfigBase<K> {
            Dictionary<K, V> ret = null;
            if (buffer == null || buffer.Length <= 0)
                return ret;

            MemoryStream stream = new MemoryStream(buffer);
            ret = ToObject<K, V>(stream, isLoadAll);

            return ret;
        }

        private static V ReadItem<K, V>(Dictionary<K, V> maps, K key) where V : ConfigBase<K> {
            if (maps == null || maps.Count <= 0)
                return null;
            V config;
            if (!maps.TryGetValue(key, out config) || config == null)
                return null;
            bool ret = config.ReadValue();
            if (!ret)
                return null;
            return config;
        }

        private static bool TryGetValue<K, V>(Dictionary<K, V> maps, K key, out V value) where V : ConfigBase<K> {
            value = default(V);
            if (maps == null || maps.Count <= 0)
                return false;
            value = ReadItem(maps, key);
            return true;
        }

        // 首次读取
        public static Dictionary<K, V> ToObject<K, V>(Stream stream, bool isLoadAll = false) where V : ConfigBase<K> {
            if (stream == null)
                return null;
            ConfigFileHeader header = new ConfigFileHeader();
            if (!header.LoadFromStream(stream) || !header.IsVaild)
                return null;

            // 读取索引
            stream.Seek(header.indexOffset, SeekOrigin.Begin);

            bool isListValue = FilePathMgr.Instance.ReadBool(stream);
            if (isListValue)
                return null;

            Dictionary<K, V> maps = null;
            for (uint i = 0; i < header.Count; ++i) {
                V config = Activator.CreateInstance<V>();
                config.stream = stream;
                K key = config.ReadKey();
                config.dataOffset = FilePathMgr.Instance.ReadLong(stream);
                if (maps == null)
                    maps = new Dictionary<K, V>((int)header.Count);
                maps[key] = config;
            }

            if (isLoadAll && maps != null && maps.Count > 0) {
                var iter = maps.GetEnumerator();
                while (iter.MoveNext()) {
                    V config = iter.Current.Value;
                    stream.Seek(config.dataOffset, SeekOrigin.Begin);
                    config.ReadValue();
                }
                iter.Dispose();
            }

            return maps;
        }

        public static Dictionary<K, List<V>> ToObjectList<K, V>(Stream stream, bool isLoadAll = false) where V : ConfigBase<K> {
            if (stream == null)
                return null;
            ConfigFileHeader header = new ConfigFileHeader();
            if (!header.LoadFromStream(stream) || !header.IsVaild)
                return null;
            // 读取索引
            stream.Seek(header.indexOffset, SeekOrigin.Begin);

            bool isListValue = FilePathMgr.Instance.ReadBool(stream);
            if (!isListValue)
                return null;

            Dictionary<K, List<V>> maps = null;
            for (uint i = 0; i < header.Count; ++i) {
                V config = Activator.CreateInstance<V>();
                config.stream = stream;
                K key = config.ReadKey();
                long dataOffset = FilePathMgr.Instance.ReadLong(stream);
                config.dataOffset = dataOffset;
                int listCnt = FilePathMgr.Instance.ReadInt(stream);
                if (maps == null)
                    maps = new Dictionary<K, List<V>>((int)header.Count);
                List<V> vs = new List<V>(listCnt);
                maps[key] = vs;
                vs.Add(config);
                for (int j = 1; j < listCnt; ++j) {
                    config = Activator.CreateInstance<V>();
                    config.stream = stream;
                    config.dataOffset = dataOffset;
                    vs.Add(config);
                }
            }

            if (isLoadAll && maps != null && maps.Count > 0) {
                var iter = maps.GetEnumerator();
                while (iter.MoveNext()) {
                    List<V> vs = iter.Current.Value;
                    V v = vs[0];
                    stream.Seek(v.dataOffset, SeekOrigin.Begin);
                    for (int i = 0; i < vs.Count; ++i) {
                        v = vs[i];
                        v.ReadValue();
                    }
                }
                iter.Dispose();
            }


            return maps;
        }

        public static bool ToStream<K, V>(Stream stream, Dictionary<K, List<V>> values) where V : ConfigBase<K> {
            if (stream == null || values == null || values.Count <= 0)
                return false;

            ConfigFileHeader header = new ConfigFileHeader((uint)values.Count, 0);
            header.SaveToStream(stream);

            
            var iter = values.GetEnumerator();
            while (iter.MoveNext()) {
                List<V> vs = iter.Current.Value;
                long dataOffset = stream.Position;
                for (int i = 0; i < vs.Count; ++i) {
                    V v = vs[i];
                    v.stream = stream;
                    v.dataOffset = dataOffset;
                    v.WriteValue();
                }
            }
            iter.Dispose();

            long indexOffset = stream.Position;
            // 是否是List
            FilePathMgr.Instance.WriteBool(stream, true);
            // 写入索引
            iter = values.GetEnumerator();
            while (iter.MoveNext()) {
                K key = iter.Current.Key;
                List<V> vs = iter.Current.Value;
                vs[0].WriteKey(key);
                // 偏移
                FilePathMgr.Instance.WriteLong(stream, vs[0].dataOffset);
                // 数量
                FilePathMgr.Instance.WriteInt(stream, vs.Count);
            }
            iter.Dispose();

            // 重写Header
            header.indexOffset = indexOffset;
            header.SeekFileToHeader(stream);
            header.SaveToStream(stream);

            return true;
        }

        public static bool ToStream<K, V>(Stream stream, Dictionary<K, V> values) where V : ConfigBase<K> {
            if (stream == null || values == null || values.Count <= 0)
                return false;

            ConfigFileHeader header = new ConfigFileHeader((uint)values.Count, 0);
            header.SaveToStream(stream);

            var iter = values.GetEnumerator();
            while (iter.MoveNext()) {
                V v = iter.Current.Value;
                v.stream = stream;
                v.dataOffset = stream.Position;
                v.WriteValue();
            }
            iter.Dispose();

            long indexOffset = stream.Position;

            // 是否是List
            FilePathMgr.Instance.WriteBool(stream, false);
            // 写入索引
            iter = values.GetEnumerator();
            while (iter.MoveNext()) {
                K key = iter.Current.Key;
                V v = iter.Current.Value;
                v.WriteKey(key);
                FilePathMgr.Instance.WriteLong(stream, v.dataOffset);
            }
            iter.Dispose();

            // 重写Header
            header.indexOffset = indexOffset;
            header.SeekFileToHeader(stream);
            header.SaveToStream(stream);

            return true;
        }


    }
}