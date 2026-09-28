# Renders the FlowBoard app icon and the .flowboard document icon.
# Usage: pip install pillow numpy && python tools/make_icons.py src/FlowBoard/Assets
import numpy as np, sys, os
from PIL import Image
N=1024
yy,xx=np.mgrid[0:N,0:N].astype(np.float32)+0.5

def bez(p0,p1,p2,p3,n=160):
    t=np.linspace(0,1,n)[:,None]
    return ((1-t)**3)*np.array(p0)+3*((1-t)**2)*t*np.array(p1)+3*(1-t)*t*t*np.array(p2)+t**3*np.array(p3)

def dist_poly(pts,X,Y):
    d=np.full(X.shape,1e9,np.float32)
    for a,b in zip(pts[:-1],pts[1:]):
        ax,ay=a; bx,by=b
        vx,vy=bx-ax,by-ay; L=vx*vx+vy*vy or 1e-9
        t=np.clip(((X-ax)*vx+(Y-ay)*vy)/L,0,1)
        dx=X-(ax+t*vx); dy=Y-(ay+t*vy)
        d=np.minimum(d,np.sqrt(dx*dx+dy*dy))
    return d

def cov(d): return np.clip(0.5-d,0,1)

def lerp(c1,c2,t): return c1*(1-t)[...,None]+c2*t[...,None]
def hexc(h): return np.array([int(h[i:i+2],16) for i in (1,3,5)],np.float32)

def emblem(X,Y,s=1.0,ox=0,oy=0):
    """Returns (coverage, rgb) of the F-flow emblem drawn in a 1024 box, scaled by s and offset."""
    X=(X-ox)/s; Y=(Y-oy)/s
    stem=dist_poly(np.array([[330,300],[330,742]]),X,Y)-62
    top=dist_poly(bez((330,300),(470,300),(560,236),(706,236)),X,Y)-56
    mid=dist_poly(bez((330,540),(430,540),(490,494),(600,494)),X,Y)-50
    shape=np.minimum(np.minimum(stem,top),mid)
    n1=np.sqrt((X-734)**2+(Y-236)**2)-78
    n2=np.sqrt((X-628)**2+(Y-494)**2)-66
    nodes=np.minimum(n1,n2)
    body=np.minimum(shape,nodes)
    # gradient: amber (bottom-left) -> pink -> violet (top-right)
    t=np.clip(((X-268)+(760-Y))/900,0,1)
    c1,c2,c3=hexc('#FFB23F'),hexc('#FF4D8D'),hexc('#8F6BFF')
    col=np.where((t<0.5)[...,None],lerp(c1,c2,t*2),lerp(c2,c3,(t-0.5)*2))
    # white cores in the nodes: the "connection points"
    core=np.minimum(np.sqrt((X-734)**2+(Y-236)**2)-32,np.sqrt((X-628)**2+(Y-494)**2)-26)
    wc=cov(core*s)[...,None]
    col=col*(1-wc)+hexc('#FFFFFF')*wc
    return cov(body*s),col

def squircle(X,Y,x0,y0,x1,y1,n=5.0):
    cx,cy=(x0+x1)/2,(y0+y1)/2; rx,ry=(x1-x0)/2,(y1-y0)/2
    v=(np.abs((X-cx)/rx)**n+np.abs((Y-cy)/ry)**n)**(1/n)
    return (v-1)*min(rx,ry)  # approx distance

def app_icon():
    rgba=np.zeros((N,N,4),np.float32)
    d=squircle(xx,yy,40,40,984,984)
    a=cov(d)
    t=np.clip((xx+yy)/(2*N),0,1)
    bg=lerp(hexc('#25195E'),hexc('#0C0B24'),t)
    # soft glow behind the emblem
    g=np.exp(-(((xx-560)**2+(yy-470)**2)/(2*260**2)))[...,None]
    bg=bg+g*np.array([70,30,90],np.float32)*0.55
    # thin light rim
    rim=np.clip(1-np.abs(d+6)/3,0,1)[...,None]*0.18
    bg=bg*(1-rim)+255*rim
    ec,ecol=emblem(xx,yy,0.92,40,48)
    col=bg*(1-ec[...,None])+ecol*ec[...,None]
    rgba[...,:3]=col; rgba[...,3]=a*255
    return Image.fromarray(np.clip(rgba,0,255).astype(np.uint8),'RGBA')

def doc_icon():
    rgba=np.zeros((N,N,4),np.float32)
    x0,y0,x1,y1,f=170,60,854,964,190
    # page with a folded top-right corner
    inside=(xx>=x0)&(xx<=x1)&(yy>=y0)&(yy<=y1)&~((xx>x1-f)&(yy<y0+f)&((xx-(x1-f))>(yy-y0)))
    # rounded feel: soften by blurring edges with distance to rect
    dx=np.maximum(np.maximum(x0-xx,xx-x1),0); dy=np.maximum(np.maximum(y0-yy,yy-y1),0)
    page=np.where(inside,1.0,0.0)
    col=np.zeros((N,N,3),np.float32)+hexc('#F6F4FF')
    # top band in the app's indigo
    band=(yy<y0+250)
    col=np.where(band[...,None],lerp(hexc('#2A1D6B'),hexc('#15123A'),np.clip((xx-x0)/(x1-x0),0,1)),col)
    # fold triangle
    fold=(xx>x1-f)&(yy<y0+f)&((xx-(x1-f))<=(yy-y0))
    col=np.where(fold[...,None],hexc('#D9D3F5'),col)
    # outline
    edge=inside&((xx<x0+10)|(xx>x1-10)|(yy>y1-10)|(yy<y0+10))
    col=np.where(edge[...,None],hexc('#CFC8EE'),col)
    ec,ecol=emblem(xx,yy,0.62,200,330)
    col=col*(1-ec[...,None])+ecol*ec[...,None]
    rgba[...,:3]=col; rgba[...,3]=page*255
    img=Image.fromarray(np.clip(rgba,0,255).astype(np.uint8),'RGBA')
    return img

out=sys.argv[1]
app=app_icon(); doc=doc_icon()
sizes=[16,20,24,32,40,48,64,128,256]
app.resize((256,256),Image.LANCZOS).save(os.path.join(out,'app.png'))
app.resize((512,512),Image.LANCZOS).save(os.path.join(out,'app-512.png'))
app.save(os.path.join(out,'app.ico'),sizes=[(s,s) for s in sizes])
doc.save(os.path.join(out,'project.ico'),sizes=[(s,s) for s in sizes])
doc.resize((256,256),Image.LANCZOS).save(os.path.join(out,'project-preview.png'))
print('ok')
