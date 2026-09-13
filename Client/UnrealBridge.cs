using System.Runtime.InteropServices;

namespace OblivionMenuSkip;

// UE4SS exposes these C++ wrappers with the Windows x64 ABI. Property offsets come from
// reflection; Native.tsv contains only the menu functions used by this mod.
internal sealed class UnrealBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet=CharSet.Unicode)] private delegate nint Find(string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet=CharSet.Unicode)] private delegate nint Named(nint self,string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Ref(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Event(nint self,nint fn,nint args);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet=CharSet.Unicode)] private delegate nint FindObject(nint cls,nint outer,string name,[MarshalAs(UnmanagedType.I1)] bool exact);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    private readonly Find _find;
    private readonly FindObject _object;
    private readonly Named _property, _function;
    private readonly Ref _offset, _elementSize, _paramSize, _children, _next, _name;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NameIndex(nint name);
    private readonly NameIndex _index;
    private readonly HashSet<string> _resolved=[];
    private readonly Event _event;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void WeakInit(nint self,nint obj);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint WeakGet(nint self);
    private readonly WeakInit _weakInit;
    private readonly WeakGet _weakGet;
    private readonly Dictionary<string,NativeFunctionLayout> _layouts=new(StringComparer.Ordinal);
    public UnrealBridge()
    {
        var module=GetModuleHandle("UE4SS.dll"); if(module==0) throw new NotSupportedException("UE4SS is not loaded.");
        T Bind<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
        _find=Bind<Find>("?FindFirstOf@UObjectGlobals@Unreal@RC@@YAPEAVUObject@23@PEB_W@Z");
        _object=Bind<FindObject>("?StaticFindObject_InternalSlow@UObjectGlobals@Unreal@RC@@YAPEAVUObject@23@PEAVUClass@23@PEAV423@PEB_W_N@Z");
        _property=Bind<Named>("?GetPropertyByNameInChain@UObject@Unreal@RC@@QEAAPEAVFProperty@23@PEB_W@Z");
        _function=Bind<Named>("?GetFunctionByNameInChain@UObject@Unreal@RC@@QEAAPEAVUFunction@23@PEB_W@Z");
        _offset=Bind<Ref>("?GetOffset_Internal@FProperty@Unreal@RC@@QEAAAEAHXZ");
        _elementSize=Bind<Ref>("?GetElementSize@FProperty@Unreal@RC@@QEAAAEAHXZ");
        _paramSize=Bind<Ref>("?GetParmsSize@UFunction@Unreal@RC@@QEAAAEAGXZ");
        _children=Bind<Ref>("?GetChildProperties@UStruct@Unreal@RC@@QEAAAEAPEAVFField@23@XZ");
        _next=Bind<Ref>("?GetNext@FField@Unreal@RC@@AEAAAEAPEAV123@XZ");
        _name=Bind<Ref>("?GetNamePrivate@FField@Unreal@RC@@AEAAAEAVFName@23@XZ");
        _index=Bind<NameIndex>("?GetComparisonIndex@FName@Unreal@RC@@QEBAIXZ");
        _event=Bind<Event>("?ProcessEvent@UObject@Unreal@RC@@QEAAXPEAVUFunction@23@PEAX@Z");
        _weakInit=Bind<WeakInit>("??0FWeakObjectPtr@Unreal@RC@@QEAA@PEAVUObject@12@@Z");
        _weakGet=Bind<WeakGet>("?Get@FWeakObjectPtr@Unreal@RC@@QEBAPEAVUObject@23@XZ");
        using var stream=typeof(UnrealBridge).Assembly.GetManifestResourceStream("OblivionMenuSkip.Native.tsv")!;
        using var reader=new StreamReader(stream);
        _layouts=NativeLayoutTable.Read(reader);
    }
    public byte[] ResultBytes(byte[] data,string fn,string field="ReturnValue") {var f=_layouts[fn].Fields[field];return data.AsSpan(f.Offset,f.Size).ToArray();}
    private NativeFunctionLayout Resolve(string name,nint fn,NativeFunctionLayout expected) {
        if(_resolved.Contains(name))return expected;
        var props=new List<(uint Name,int Offset,int Size)>();
        for(nint p=Marshal.ReadIntPtr(_children(fn));p!=0;p=Marshal.ReadIntPtr(_next(p))) {
            if(props.Count>=64)throw new NotSupportedException("Unexpected parameter chain.");
            props.Add((_index(_name(p)),Marshal.ReadInt32(_offset(p)),Marshal.ReadInt32(_elementSize(p))));
        }
        int size=(ushort)Marshal.ReadInt16(_paramSize(fn));
        var fields=new Dictionary<string,(int Offset,int Size)>();
        if(name=="Conv_StringToName") {
            // Bootstrap by reflected parameter offset/type width; FName is 8 or 12 bytes.
            if(props.Count!=2 || !props.Any(p=>p.Offset==0&&p.Size==16))throw new NotSupportedException("String conversion ABI changed.");
            var ret=props.Single(p=>p.Offset==16&&(p.Size==8||p.Size==12));
            fields.Add("InString",(0,16));fields.Add("ReturnValue",(ret.Offset,ret.Size));
        } else foreach(var key in expected.Fields.Keys) {
            var raw=Call(DefaultObject("/Script/Engine.Default__KismetStringLibrary"),"Conv_StringToName",("InString",key));
            var bytes=ResultBytes(raw,"Conv_StringToName");var mem=Marshal.AllocHGlobal(bytes.Length);
            try {Marshal.Copy(bytes,0,mem,bytes.Length);uint id=_index(mem);var p=props.Single(p=>p.Name==id);fields.Add(key,(p.Offset,p.Size));}finally{Marshal.FreeHGlobal(mem);}
        }
        foreach(var f in fields.Values)if(f.Offset<0||f.Size<0||f.Offset+f.Size>size)throw new NotSupportedException("Parameter bounds invalid.");
        var layout=new NativeFunctionLayout(size,fields);_layouts[name]=layout;_resolved.Add(name);return layout;
    }    public nint FindInstance(string className)=>_find(className);
    public nint DefaultObject(string path)=>_object(0,0,path,false);
    private (nint Property,nint Address) Field(nint obj,string name,int size=0)
    {
        if(obj==0) throw new InvalidOperationException("Missing Unreal object.");
        var p=_property(obj,name); if(p==0) throw new MissingMemberException(name);
        int offset=Marshal.ReadInt32(_offset(p)); int actual=Marshal.ReadInt32(_elementSize(p));
        if(offset<0 || offset>1_048_576 || (size!=0 && actual!=size)) throw new NotSupportedException($"Property ABI mismatch: {name}");
        return (p,obj+offset);
    }
    public nint Object(nint obj,string name)=>Marshal.ReadIntPtr(Field(obj,name,8).Address);
    public NativeWeakObject Weak(nint obj){
        nint memory=Marshal.AllocHGlobal(8);
        try{Marshal.WriteInt64(memory,0);_weakInit(memory,obj);return new(Marshal.ReadInt32(memory),Marshal.ReadInt32(memory,4));}
        finally{Marshal.FreeHGlobal(memory);}
    }
    public nint ResolveWeak(NativeWeakObject weak){
        nint memory=Marshal.AllocHGlobal(8);
        try{Marshal.WriteInt32(memory,weak.Index);Marshal.WriteInt32(memory,4,weak.Serial);return _weakGet(memory);}
        finally{Marshal.FreeHGlobal(memory);}
    }
    public byte[] Call(nint obj,string name,params (string Name,object Value)[] values)
    {
        if(obj==0) throw new InvalidOperationException($"Missing target for {name}");
        var fn=_function(obj,name); if(fn==0) throw new MissingMethodException(name);
        var layout=_layouts[name];
        layout=Resolve(name,fn,layout);
        if((ushort)Marshal.ReadInt16(_paramSize(fn))!=layout.Size) throw new NotSupportedException($"UE function ABI mismatch: {name}");
        var data=new byte[layout.Size]; var strings=new List<nint>(); var buffer=Marshal.AllocHGlobal(Math.Max(1,layout.Size));
        try {
            foreach(var (key,value) in values) {
                var field=layout.Fields[key]; byte[] bytes=value switch {
                    nint p=>BitConverter.GetBytes(p.ToInt64()), int i=>BitConverter.GetBytes(i), float f=>BitConverter.GetBytes(f),
                    bool b=>[b ? (byte)1 : (byte)0], byte b=>[b],
                    byte[] raw=>raw,
                    string s=>StringBytes(s), _=>throw new ArgumentException("Unsupported Unreal argument")
                };
                if(bytes.Length!=field.Size) throw new NotSupportedException($"Argument ABI mismatch: {name}.{key}");
                bytes.CopyTo(data,field.Offset);
            }
            Marshal.Copy(data,0,buffer,data.Length); _event(obj,fn,buffer); Marshal.Copy(buffer,data,0,data.Length); return data;
        } finally { Marshal.FreeHGlobal(buffer); foreach(var p in strings) Marshal.FreeHGlobal(p); }
        byte[] StringBytes(string s) { var p=Marshal.StringToHGlobalUni(s); strings.Add(p); return [..BitConverter.GetBytes(p.ToInt64()),..BitConverter.GetBytes(s.Length+1),..BitConverter.GetBytes(s.Length+1)]; }
    }
    public nint Pointer(nint obj,string name,params (string Name,object Value)[] args) { var b=Call(obj,name,args); return (nint)BitConverter.ToInt64(b,_layouts[name].Fields["ReturnValue"].Offset); }
    public bool Boolean(nint obj,string name) { var b=Call(obj,name); return b[_layouts[name].Fields["ReturnValue"].Offset]!=0; }
}

internal readonly record struct NativeWeakObject(int Index,int Serial);
