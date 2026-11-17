import json, subprocess, sys, threading, time, os
class Mcp:
    def __init__(self, args, log=None):
        env=dict(os.environ, DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="1")
        self.p=subprocess.Popen(args,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=open(log or '/dev/null','w'),text=True,bufsize=1,env=env)
        self.id=0
        r=self.req("initialize",{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"harness","version":"0"}})
        self.server=r
        self.note("notifications/initialized",{})
    def note(self,m,p): self.p.stdin.write(json.dumps({"jsonrpc":"2.0","method":m,"params":p})+"\n"); self.p.stdin.flush()
    def req(self,m,p):
        self.id+=1; i=self.id
        self.p.stdin.write(json.dumps({"jsonrpc":"2.0","id":i,"method":m,"params":p})+"\n"); self.p.stdin.flush()
        while True:
            line=self.p.stdout.readline()
            if not line: raise RuntimeError("server exited")
            msg=json.loads(line)
            if msg.get("id")==i: 
                if "error" in msg: return {"_rpc_error":msg["error"]}
                return msg["result"]
    def call(self,name,args):
        t=time.time()
        r=self.req("tools/call",{"name":name,"arguments":args})
        dt=time.time()-t
        out={"_ms":int(dt*1000),"isError":r.get("isError",False) if isinstance(r,dict) else None}
        if "_rpc_error" in r: out["rpc_error"]=r["_rpc_error"]; return out
        if r.get("structuredContent") is not None: out["data"]=r["structuredContent"]
        else:
            texts=[c.get("text") for c in r.get("content",[]) if c.get("type")=="text"]
            try: out["data"]=json.loads(texts[0]) if len(texts)==1 else texts
            except Exception: out["data"]=texts
        return out
    def close(self):
        try: self.p.stdin.close(); self.p.wait(10)
        except Exception: self.p.kill()
