using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class MapSpawnSource
    {
        internal readonly int Argument;
        private MapSpawnSource(int argument) { Argument = argument; }
        internal static MapSpawnSource Find(MethodInfo method)
        {
            int[] maps=method.GetParameters().Select((p,i)=>new { p.ParameterType,Index=i })
                .Where(p=>p.ParameterType==typeof(Map)).Select(p=>p.Index).ToArray();
            return maps.Length==1 ? new MapSpawnSource(maps[0]+(method.IsStatic?0:1)) : null;
        }
        internal bool Proven(MethodInfo method, RegenerationEvidence evidence)
        {
            var reader=new LifeIlReader();
            var code=reader.Read(method);
            if(code==null)return false;
            bool directGeneration=code.Any(line=>(line.opcode==OpCodes.Call||line.opcode==OpCodes.Callvirt)
                &&line.operand is MethodInfo call&&call.DeclaringType==typeof(PawnGenerator)&&call.Name==nameof(PawnGenerator.GeneratePawn));
            if(!directGeneration&&(evidence&(RegenerationEvidence.Generation|RegenerationEvidence.Unknown))!=RegenerationEvidence.Generation)return false;
            if(code.Any(line=>(line.opcode==OpCodes.Starg||line.opcode==OpCodes.Starg_S
                ||line.opcode==OpCodes.Ldarga||line.opcode==OpCodes.Ldarga_S)&&Convert.ToInt32(line.operand)==Argument))return false;
            for(int i=0;i<code.Count;i++)
            {
                if(code[i].opcode!=OpCodes.Call&&code[i].opcode!=OpCodes.Callvirt
                    ||code[i].operand is not MethodInfo call||code[i].labels.Count!=0)continue;
                if(!(call.DeclaringType==typeof(GenSpawn)&&call.Name==nameof(GenSpawn.Spawn))
                    && !(typeof(Thing).IsAssignableFrom(call.DeclaringType)&&call.Name==nameof(Thing.SpawnSetup)))continue;
                var parameters=call.GetParameters();
                int map=Array.FindIndex(parameters,p=>p.ParameterType==typeof(Map));
                if(map<0)continue;
                int cursor=i-1;
                for(int p=parameters.Length-1;p>map;p--)
                {
                    if(cursor<0||!SingleValue(code[cursor])){cursor=-1;break;}
                    cursor--;
                }
                if(cursor>=0&&code[cursor].labels.Count==0&&(LoadedArgument(code[cursor])==Argument
                    ||MapArgumentCapture.Matches(code,cursor,Argument,reader)))return true;
            }
            return false;
        }
        private static bool SingleValue(CodeInstruction line)
        {
            if(line.labels.Count!=0)return false;
            OpCode op=line.opcode;
            return op==OpCodes.Ldnull||op==OpCodes.Ldsfld||op.Name.StartsWith("ldc.",StringComparison.Ordinal)
                ||LoadedArgument(line)>=0||op.Name.StartsWith("ldloc",StringComparison.Ordinal)&&op!=OpCodes.Ldloca&&op!=OpCodes.Ldloca_S
                ||op==OpCodes.Call&&line.operand is MethodInfo method&&method.IsStatic
                    &&method.GetParameters().Length==0&&method.ReturnType!=typeof(void);
        }
        internal static int LoadedArgument(CodeInstruction line)
            => line.opcode==OpCodes.Ldarg_0?0:line.opcode==OpCodes.Ldarg_1?1:line.opcode==OpCodes.Ldarg_2?2
                :line.opcode==OpCodes.Ldarg_3?3:line.opcode==OpCodes.Ldarg||line.opcode==OpCodes.Ldarg_S?Convert.ToInt32(line.operand):-1;
    }
}
