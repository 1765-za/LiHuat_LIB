using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class MapArgumentCapture
    {
        internal static bool Matches(List<CodeInstruction> code,int position,int argument,LifeIlReader reader)
        {
            if(code[position].opcode!=OpCodes.Ldfld||code[position].operand is not FieldInfo field
                ||field.FieldType!=typeof(Map)||!field.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute),false)
                ||position<1||code[position-1].labels.Count!=0)return false;
            int owner=Local(code[position-1],false);
            if(owner<0)return false;
            int[] assignments=Enumerable.Range(0,code.Count).Where(i=>Local(code[i],true)==owner).ToArray();
            if(assignments.Length!=1||assignments[0]<1||code[assignments[0]-1].opcode!=OpCodes.Newobj
                ||code[assignments[0]-1].operand is not ConstructorInfo constructor||constructor.DeclaringType!=field.DeclaringType)return false;
            int[] writes=Enumerable.Range(0,code.Count).Where(i=>code[i].opcode==OpCodes.Stfld&&Equals(code[i].operand,field)).ToArray();
            if(writes.Length!=1)return false;
            int write=writes[0];
            if(write<2||MapSpawnSource.LoadedArgument(code[write-1])!=argument||Local(code[write-2],false)!=owner)return false;
            if(code.Any(line=>line.opcode==OpCodes.Ldflda&&Equals(line.operand,field)
                ||(line.opcode==OpCodes.Ldloca||line.opcode==OpCodes.Ldloca_S)&&Index(line)==owner))return false;
            foreach(MethodInfo method in LifeDiscoveryMetadata.Methods(field.DeclaringType,
                BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))
            {
                var body=reader.Read(method);
                if(body==null||body.Any(line=>(line.opcode==OpCodes.Stfld||line.opcode==OpCodes.Ldflda)&&Equals(line.operand,field)))return false;
            }
            return true;
        }
        private static int Local(CodeInstruction line,bool store)
        {
            string prefix=store?"stloc":"ldloc";
            string name=line.opcode.Name;
            if(!name.StartsWith(prefix,StringComparison.Ordinal)||name.StartsWith("ldloca",StringComparison.Ordinal))return -1;
            char last=name[name.Length-1];
            return last>='0'&&last<='3'?last-'0':Index(line);
        }
        private static int Index(CodeInstruction line)=>line.operand is LocalBuilder local?local.LocalIndex
            :line.operand is LocalVariableInfo variable?variable.LocalIndex:Convert.ToInt32(line.operand);
    }
}
