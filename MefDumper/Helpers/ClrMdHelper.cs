using MefDumper.DataModel;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using WcfDumper.DataModel;

namespace WcfDumper.Helpers
{
    public static class ClrMdHelper
    {
        public static DataTargetWrapper LoadDumpFile(string file)
        {
            return new DataTargetWrapper(file);
        }

        public static DataTargetWrapper AttachToLiveProcess(int pid)
        {
            return new DataTargetWrapper(pid);
        }

        public static List<ulong> GetLastObjectInHierarchyAsArray(ClrHeap heap, ulong obj, string[] hierarchy, int currentIndex, string arrayTypeToVerify)
        {
            ulong arrayObj = GetLastObjectInHierarchy(heap, obj, hierarchy, currentIndex);
            ClrType arrayType = heap.GetObjectType(arrayObj);

            Debug.Assert(arrayType.Name == arrayTypeToVerify);

            return GetArrayItems(arrayType, arrayObj);
        }

        public static List<KVP> GetLastObjectInHierarchyAsKVPs(ClrHeap heap, ulong obj, string[] hierarchy, int currentIndex, string arrayTypeToVerify)
        {
            ulong arrayObj = GetLastObjectInHierarchy(heap, obj, hierarchy, currentIndex);
            ClrType arrayType = heap.GetObjectType(arrayObj);

            Debug.Assert(arrayType.Name == arrayTypeToVerify);

            return GetKVPs(arrayType, arrayObj);
        }

        public static List<ulong> GetArrayItems(ClrType type, ulong array)
        {
            int length = type.GetArrayLength(array);
            var ret = new List<ulong>();

            for (int i = 0; i < length; i++)
            {
                var val = (ulong)type.GetArrayElementValue(array, i);

                if (val != 0)
                {
                    ret.Add(val);
                }
            }

            return ret;
        }

        public static List<KVP> GetKVPs(ClrType type, ulong items)
        {
            int length = type.GetArrayLength(items);
            var ret = new List<KVP>();

            for (int i = 0; i < length; i++)
            {
                ulong addr = type.GetArrayElementAddress(items, i);                

                var keyField = type.ComponentType.GetFieldByName("key");
                var key = (ulong)keyField.GetValue(addr-(ulong)IntPtr.Size);

                // no need to add these pre-allocated entries
                if (key == 0) continue;

                var valueField = type.ComponentType.GetFieldByName("value");
                var value = (ulong)valueField.GetValue(addr - (ulong)IntPtr.Size);

                ret.Add(new KVP(key, value));
            }

            return ret;
        }

        public static ulong GetLastObjectInHierarchy(ClrHeap heap, ulong heapobject, string[] hierarchy, int currentIndex)
        {
            ClrType type = heap.GetObjectType(heapobject);
            ClrInstanceField field = type.GetFieldByName(hierarchy[currentIndex]);
            ulong fieldValue = (ulong)field.GetValue(heapobject, false, false);

            currentIndex++;
            if (currentIndex == hierarchy.Length)
            {
                return fieldValue;
            }

            return GetLastObjectInHierarchy(heap, fieldValue, hierarchy, currentIndex);
        }

        public static T GetObjectAs<T>(ClrHeap heap, ulong heapobject, string fieldName)
        {
            ClrType type = heap.GetObjectType(heapobject);
            ClrInstanceField field = type.GetFieldByName(fieldName);
            T fieldValue = (T)field.GetValue(heapobject);

            return fieldValue;
        }

        public static string GetStringContents(ClrHeap heap, ulong strAddr)
        {
            if (strAddr == 0L)
            {
                return null;
            }

            ClrType clrType = heap.GetObjectType(strAddr);
            var firstCharField = clrType.GetFieldByName("m_firstChar");
            var stringLengthField = clrType.GetFieldByName("m_stringLength");
            
            int length = 0;
            if (stringLengthField != null)
            {
                length = (int)stringLengthField.GetValue(strAddr);
            }
            
            if (length == 0)
            {
                return "";
            }

            ulong data2 = firstCharField.GetAddress(strAddr);
            byte[] buffer = new byte[length * 2];

            if (!heap.Runtime.ReadMemory(data2, buffer, buffer.Length, out int _))
            {
                return null;
            }

            return Encoding.Unicode.GetString(buffer);
        }
    }
}
