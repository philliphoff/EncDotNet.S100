import fiona, glob, numpy as np, os, sys
from rasterio import features as rf
from rasterio.transform import from_origin
from shapely.geometry import shape, Point, mapping
import os
S,N,W,E=[float(x) for x in os.environ.get("BBOX","48.33,48.80,-123.25,-122.45").split(",")]
DLAT=0.000225; DLON=0.00034   # ~25 m
H=int((N-S)/DLAT); Wd=int((E-W)/DLON)
T=from_origin(W,N,DLON,DLAT)
cells=sorted(glob.glob(os.environ.get('CELLS','/home/claude/work/enc/cells')+'/ENC_ROOT/*/*.000'))
def cscale(p):
    try:
        with fiona.open(p,layer='DSID') as l:
            for f in l: return f['properties'].get('DSPM_CSCL') or 0
    except Exception: return 0
order=sorted(((cscale(p),p) for p in cells), key=lambda x:-x[0])  # coarse first
depth=np.full((H,Wd),np.nan,dtype=np.float32)    # min depth (DRVAL1); -99 = land
haz=np.zeros((H,Wd),dtype=np.uint8)
def layers(p):
    try: return fiona.listlayers(p)
    except Exception: return []
for sc,p in order:
    if sc>200000: continue
    L=layers(p)
    cov=[]
    if 'M_COVR' in L:
        with fiona.open(p,layer='M_COVR') as l:
            cov=[(f['geometry'],1) for f in l if f['properties'].get('CATCOV')==1 and f['geometry']]
    if not cov: continue
    cm=rf.rasterize(cov,out_shape=(H,Wd),transform=T,fill=0,dtype='uint8').astype(bool)
    if not cm.any(): continue
    local=np.full((H,Wd),np.nan,dtype=np.float32)
    for lay,val in (('DEPARE',None),('DRGARE',None),('LNDARE',-99.0),('UNSARE',-50.0)):
        if lay not in L: continue
        with fiona.open(p,layer=lay) as l:
            shp=[]
            for f in l:
                g=f['geometry']
                if not g or g['type'] not in ('Polygon','MultiPolygon'): continue
                v=val if val is not None else f['properties'].get('DRVAL1')
                if v is None: v=-50.0 if lay=='DEPARE' else 2.0
                shp.append((g,float(v)))
            if shp:
                r=rf.rasterize(shp,out_shape=(H,Wd),transform=T,fill=np.nan,dtype='float32')
                m=~np.isnan(r)
                local[m]=np.where(np.isnan(local[m]),r[m],np.minimum(local[m],r[m]))
    m=cm & ~np.isnan(local)
    depth[m]=local[m]
    # point hazards (rocks, obstructions, wrecks) shallower than 3m or unknown
    for lay in ('UWTROC','OBSTRN','WRECKS'):
        if lay not in L: continue
        with fiona.open(p,layer=lay) as l:
            shp=[]
            for f in l:
                g=f['geometry']
                if not g: continue
                v=f['properties'].get('VALSOU')
                if v is not None and v>=4: continue
                geom=shape(g)
                shp.append((mapping(geom.buffer(0.0009) if geom.geom_type=='Point' else geom.buffer(0.0004)),1))
            if shp:
                r=rf.rasterize(shp,out_shape=(H,Wd),transform=T,fill=0,dtype='uint8')
                haz|=(r.astype(bool)&cm).astype(np.uint8)
    print(sc,os.path.basename(p),int(m.sum()),file=sys.stderr)
np.savez_compressed(os.environ.get('OUT','grid.npz'),depth=depth,haz=haz,meta=np.array([S,N,W,E,DLAT,DLON]))
print(H,Wd)
