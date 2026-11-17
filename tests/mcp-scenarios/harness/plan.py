import numpy as np, heapq, json, sys
from scipy import ndimage
import os
g=np.load(os.environ.get('GRID','grid.npz')); depth=g['depth']; haz=g['haz'].astype(bool)
S,N,W,E,DLAT,DLON=g['meta']
H,Wd=depth.shape
def rc(lat,lon): return int((N-lat)/DLAT), int((lon-W)/DLON)
def ll(r,c): return (N-(r+0.5)*DLAT, W+(c+0.5)*DLON)
def plan(pts, contour=3.0, near_contour=1.2, near_m=700, margin_cells=2):
    nav=(np.nan_to_num(depth,nan=-99)>=contour)&~haz
    dist=ndimage.distance_transform_edt(nav)
    out=[]
    for (a,b) in zip(pts[:-1],pts[1:]):
        out_leg=astar(nav,dist,a,b,near_contour,near_m,margin_cells)
        out+= out_leg if not out else out_leg[1:]
    return out
def local_ok(nav,dist,ends,near_contour,near_m,margin):
    # returns cost-able mask
    ok=nav&(dist>=margin)
    rr,cc=np.ogrid[:H,:Wd]
    for (lat,lon) in ends:
        r,c=rc(lat,lon); rad=int(near_m/25)
        m=((rr-r)**2+(cc-c)**2)<=rad*rad
        ok|= m & (np.nan_to_num(depth,nan=-99)>=near_contour)
    return ok
def astar(nav,dist,a,b,near_contour,near_m,margin):
    ok=local_ok(nav,dist,[a,b],near_contour,near_m,margin)
    s=rc(*a); t=rc(*b)
    for p in (s,t): ok[p]=True
    pen=np.where(dist<8, (8-np.minimum(dist,8))*0.6, 0)
    INF=1e18; best={s:0}; prev={}; pq=[(0,s)]
    nb=[(-1,0,1),(1,0,1),(0,-1,1),(0,1,1),(-1,-1,1.414),(-1,1,1.414),(1,-1,1.414),(1,1,1.414)]
    while pq:
        f,u=heapq.heappop(pq)
        if u==t: break
        gu=best[u]
        if f-np.hypot(u[0]-t[0],u[1]-t[1])>gu+1e-9: continue
        for dr,dc,w in nb:
            v=(u[0]+dr,u[1]+dc)
            if not(0<=v[0]<H and 0<=v[1]<Wd) or not ok[v]: continue
            ng=gu+w*(1+pen[v])
            if ng<best.get(v,INF):
                best[v]=ng; prev[v]=u; heapq.heappush(pq,(ng+np.hypot(v[0]-t[0],v[1]-t[1]),v))
    if t not in prev: raise RuntimeError(f"no path {a}->{b}")
    path=[t]
    while path[-1]!=s: path.append(prev[path[-1]])
    path.reverse()
    return simplify(path,ok,a,b)
def los(ok,p,q):
    n=int(max(abs(p[0]-q[0]),abs(p[1]-q[1]))*2)+1
    for i in range(n+1):
        r=round(p[0]+(q[0]-p[0])*i/n); c=round(p[1]+(q[1]-p[1])*i/n)
        if not ok[r,c]: return False
    return True
def simplify(path,ok,a,b):
    out=[path[0]]; i=0
    while i<len(path)-1:
        j=len(path)-1
        while j>i+1 and not los(ok,path[i],path[j]): j-=1
        out.append(path[j]); i=j
    pts=[ll(*p) for p in out]
    pts[0]=a; pts[-1]=b
    return [(round(x,5),round(y,5)) for x,y in pts]
