namespace OblivionMenuSkip;
internal sealed record NativeFunctionLayout(int Size,Dictionary<string,(int Offset,int Size)> Fields);
internal static class NativeLayoutTable
{
 internal static Dictionary<string,NativeFunctionLayout> Read(TextReader reader)
 {
  var result=new Dictionary<string,NativeFunctionLayout>(StringComparer.Ordinal);
  int number=0;
  while(reader.ReadLine() is {} raw){
   number++;string line=raw.Trim();if(line.Length==0||line.StartsWith('#'))continue;
   var parts=line.Split('|');
   InvalidDataException Invalid()=>new($"Invalid native layout at line {number}: {parts[0]}");
   if(parts.Length<2||parts[0].Length==0||!int.TryParse(parts[1],out var size)||size<0||size>65535)throw Invalid();
   var fields=new Dictionary<string,(int Offset,int Size)>(StringComparer.Ordinal);
   foreach(var part in parts.Skip(2)){
    var values=part.Split(':');
    if(values.Length!=3||values[0].Length==0||!int.TryParse(values[1],out var offset)||!int.TryParse(values[2],out var width)||offset<0||width<0||offset>size||width>size-offset||!fields.TryAdd(values[0],(offset,width)))throw Invalid();
   }
   if(!result.TryAdd(parts[0],new(size,fields)))throw Invalid();
  }
  return result;
 }
}
