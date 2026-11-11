cbuffer Scene : register(b0) { float frame; float scene; float2 padding; };
float4 vsMain(uint vertex : SV_VertexID) : SV_Position {
    float2 corner = float2((vertex << 1) & 2, vertex & 2);
    return float4(corner * float2(2,-2) + float2(-1,1),0,1);
}
static const float3 sun = float3(-0.46,0.79,0.40);

float box(float3 p, float3 b, float r){
  float3 d=abs(p)-b+r;
  return length(max(d,0.0))+min(max(d.x,max(d.y,d.z)),0.0)-r;
}
float wheel(float3 p){
  float2 d=abs(float2(length(p.xy),p.z))-float2(0.14,0.066);
  return min(max(d.x,d.y),0.0)+length(max(d,0.0));
}
float2 pick(float2 a,float d,float id){return d<a.x?float2(d,id):a;}
float2 world(float3 p){
  float2 hit=float2(p.y+0.20,1.0);
  hit=pick(hit,box(p-float3(0,-0.15,0),float3(2.63,0.15,1.55),0.13),2.0);
  hit=pick(hit,box(p-float3(-0.25,0.002,-0.90),float3(1.05,0.004,0.014),0.003),9.0);
  hit=pick(hit,box(p-float3(-1.30,0.002,-0.48),float3(0.014,0.004,0.42),0.003),9.0);

  // Two tilted photovoltaic panels, with posts underneath.
  float3 q=p-float3(-1.55,0.61,-0.28);
  q.x=abs(q.x+0.07)-0.47;
  q.yz=mul(float2x2(0.87,0.49,-0.49,0.87),q.yz);
  hit=pick(hit,box(q,float3(0.44,0.033,0.51),0.022),3.0);
  hit=pick(hit,box(q-float3(0,0.031,0),float3(0.408,0.009,0.477),0.008),4.0);
  q=p-float3(-1.62,0.26,-0.28);
  q.x=abs(q.x)-0.47;
  hit=pick(hit,box(q,float3(0.05,0.27,0.06),0.02),3.0);

  // Electric car, four tyres and panoramic roof.
  q=p-float3(0.63,0.0,0.76);
  hit=pick(hit,box(q-float3(0,0.25,0),float3(0.81,0.14,0.32),0.115),5.0);
  hit=pick(hit,box(q-float3(-0.08,0.40,0),float3(0.44,0.15,0.265),0.13),6.0);
  float3 w=float3(abs(q.x)-0.51,q.y-0.155,abs(q.z)-0.31);
  hit=pick(hit,wheel(w),0.0);
  hit=pick(hit,wheel(w-float3(0,0,0.062))+0.026,3.0);
  float3 lamps=q;
  lamps.z=abs(lamps.z)-0.22;
  hit=pick(hit,box(lamps-float3(0.775,0.28,0),float3(0.05,0.016,0.065),0.013),7.0);
  hit=pick(hit,box(lamps-float3(-0.775,0.285,0),float3(0.05,0.017,0.061),0.013),11.0);
  float3 mirrors=q;
  mirrors.z=abs(mirrors.z)-0.298;
  hit=pick(hit,box(mirrors-float3(0.21,0.399,0),float3(0.052,0.023,0.053),0.023),5.0);

  // Heat pump cabinet and front fan.
  q=p-float3(1.47,0.36,-0.60);
  hit=pick(hit,box(q,float3(0.40,0.34,0.24),0.065),8.0);
  hit=pick(hit,box(q-float3(0,0,0.244),float3(0.29,0.25,0.02),0.02),10.0);
  float fan=length(q.xy);
  float grille=abs(frac(fan*46.0)-0.5)/46.0;
  hit=pick(hit,max(max(grille-0.003,fan-0.193),abs(q.z-0.27)-0.008),3.0);
  hit=pick(hit,box(q-float3(0,0,0.27),float3(0.048,0.048,0.016),0.014),3.0);

  // Home battery with a status light.
  q=p-float3(0.32,0.46,-0.78);
  hit=pick(hit,box(q,float3(0.22,0.44,0.15),0.085),8.0);
  hit=pick(hit,box(q-float3(0,0.23,0.149),float3(0.083,0.018,0.012),0.012),9.0);
  return hit;
}
float3 normal(float3 p){
  float2 e=float2(0.002,0.0);
  return normalize(float3(world(p+e.xyy).x-world(p-e.xyy).x,
    world(p+e.yxy).x-world(p-e.yxy).x,world(p+e.yyx).x-world(p-e.yyx).x));
}
float3 colour(float id,float3 p){
  if(id<0.5)return float3(0.055,0.068,0.09);
  if(id<1.5){
    float glow=0.19*exp(-0.19*dot(p.xz,p.xz));
    return float3(0.07,0.15,0.28)+glow*float3(0.17,0.50,0.72);
  }
  if(id<2.5){
    float3 grass=float3(0.29,0.49,0.41);
    float road=(1.0-smoothstep(0.37,0.43,abs(p.z-0.77)))*smoothstep(0.0,0.07,p.y+0.07);
    float dashed=(1.0-smoothstep(0.002,0.017,abs(p.z-0.77)))*step(0.50,frac(p.x*2.8));
    return lerp(grass,lerp(float3(0.21,0.30,0.36),float3(0.55,0.64,0.67),dashed),road);
  }
  if(id<3.5)return float3(0.44,0.57,0.70);
  if(id<4.5){
    float3 q=p-float3(-1.55,0.61,-0.28);
    q.x=abs(q.x+0.07)-0.47;
    q.yz=mul(float2x2(0.87,0.49,-0.49,0.87),q.yz);
    float2 cells=abs(frac(q.xz*float2(11.0,8.0))-0.5);
    return lerp(float3(0.06,0.22,0.50),float3(0.42,0.72,0.92),smoothstep(0.43,0.5,max(cells.x,cells.y)));
  }
  if(id<5.5)return float3(0.92,0.25,0.13);
  if(id<6.5)return float3(0.07,0.14,0.23);
  if(id<7.5)return float3(0.79,0.92,1.0);
  if(id<8.5)return float3(0.79,0.87,0.90);
  if(id<9.5){
    float flow=pow(0.5+0.5*cos(15.0*(p.x-p.z)-frame*10.0),7.0);
    return lerp(float3(0.08,0.45,0.33),float3(0.48,1.0,0.75),flow);
  }
  if(id>10.5)return float3(0.94,0.08,0.035);
  float2 fan=p.xy-float2(1.47,0.36);
  float radius=length(fan);
  float blade=cos(5.0*atan2(fan.y,fan.x)-frame*14.0+radius*21.0);
  float visible=smoothstep(0.12,0.30,blade)*(1.0-smoothstep(0.17,0.21,radius));
  return lerp(float3(0.04,0.08,0.13),float3(0.37,0.54,0.66),visible);
}
float shadow(float3 p){
  float shade=1.0;
  float d=0.035;
  for(int i=0;i<12;i++){
    float h=world(p+d*sun).x;
    shade=min(shade,8.0*h/d);
    d+=clamp(h,0.025,0.26);
  }
  return clamp(shade,0.18,1.0);
}
float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float grain(float2 p){
  float2 i=floor(p), f=frac(p);
  f=f*f*(3.0-2.0*f);
  return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1.0),f.x),f.y);
}
float3 brick(float2 p){
  float2 tiled=p*float2(3.1,7.0);
  tiled.x+=fmod(floor(tiled.y),2.0)*0.5;
  float2 cell=frac(tiled);
  float2 side=min(cell,1.0-cell);
  float relief=smoothstep(0.025,0.085,min(side.x,side.y));
  float noise=0.42*grain(p*28.0)+0.39*grain(p*95.0)+0.19*grain(p*270.0);
  float shade=0.68+hash(floor(tiled))*0.28;
  float3 clay=lerp(float3(0.36,0.10,0.068),float3(0.72,0.29,0.16),noise)*shade;
  clay*=0.70+0.30*smoothstep(0.02,0.14,side.y);
  return lerp(float3(0.19,0.19,0.20)*(0.75+noise*0.4),clay,relief);
}
float2 crackCorner(int i,float ring){
  float j=float(i%10);
  float angle=j*0.62831853+(hash(float2(j,91.0))-0.5)*0.33;
  float2 direction=float2(cos(angle),sin(angle));
  float radius=ring<0.5 ? 0.27+0.22*hash(float2(j,33.0)) :
      min(0.88/max(0.001,abs(direction.x)),0.65/max(0.001,abs(direction.y)));
  return direction*radius;
}
float3 spin(float3 v,float3 axis,float a){
  return v*cos(a)+cross(axis,v)*sin(a)+axis*dot(axis,v)*(1.0-cos(a));
}
float hitTriangle(float3 eye,float3 ray,float3 a,float3 b,float3 c,out float2 uv){
  uv=0;
  float3 e=b-a, f=c-a;
  float3 h=cross(ray,f);
  float d=dot(e,h);
  if(abs(d)<0.00005)return -1.0;
  float3 s=eye-a;
  float3 q=cross(s,e);
  uv=float2(dot(s,h),dot(ray,q))/d;
  if(min(uv.x,uv.y)<0.0 || uv.x+uv.y>1.0)return -1.0;
  return dot(f,q)/d;
}
float distanceToEdge(float2 position,float2 a,float2 b){
  float2 edge=b-a, delta=position-a;
  return length(delta-edge*saturate(dot(delta,edge)/max(0.0001,dot(edge,edge))));
}
float3 workshop(float2 uv){
  float t=fmod(frame,3.7);
  float3 eye=float3(0.80*sin(frame*0.48),1.49,3.4);
  float3 aim=normalize(float3(0,1.17,-0.35)-eye);
  float3 right=normalize(cross(aim,float3(0,1,0)));
  float3 up=cross(right,aim);
  float3 ray=normalize(aim+uv.x*right+uv.y*up);
  float wall=(-1.0-eye.z)/ray.z;
  float floorDepth=-eye.y/min(ray.y,-0.0001);
  float depth=min(wall,floorDepth);
  float3 p=eye+depth*ray;
  float3 light=float3(1.6*sin(frame*0.66),2.1,0.2);
  float lighting=0.23+1.2/(1.1+dot(light-p,light-p));
  float3 colour=depth==wall ? brick(p.xy)*lighting : (0.18+grain(p.xz*17.0)*0.13)*lighting;
  colour*=float3(1.09,0.90,0.82);

  // A freestanding steel frame, irregular cracks, and the same pane breaking apart.
  float front=(0.1-eye.z)/ray.z;
  float2 panel=(eye+front*ray).xy-float2(0,1.23);
  float2 frameEdge=abs(panel)-float2(0.88,0.65);
  float metal=max(frameEdge.x,frameEdge.y);
  bool rail=metal<0.035 && metal>-0.014;
  bool feet=abs(abs(panel.x)-0.91)<0.030 && panel.y<0.66 && panel.y>-1.23;
  if((rail||feet) && front<depth){
    float edgeLight=smoothstep(-0.014,0.012,metal);
    float grainy=grain(panel*float2(510.0,15.0));
    colour=lerp(float3(0.08,0.10,0.12),float3(0.28,0.34,0.41),0.5*edgeLight+0.22*grainy)*lighting;
    depth=front;
  }
  if(t<1.2 && max(frameEdge.x,frameEdge.y)<0.0 && front<depth){
    float seamDistance=1.0;
    [loop] for(int wedge=0;wedge<10;wedge++){
      float2 inner=crackCorner(wedge,0), next=crackCorner(wedge+1,0), outer=crackCorner(wedge,1);
      seamDistance=min(seamDistance,distanceToEdge(panel,inner,next));
      seamDistance=min(seamDistance,distanceToEdge(panel,inner,outer));
      seamDistance=min(seamDistance,distanceToEdge(panel,float2(0,0),inner));
    }
    float crack=(1.0-smoothstep(0.001,0.004,seamDistance))*smoothstep(0.62,1.17,t);
    float shine=pow(saturate(dot(normalize(float3(panel,1)),normalize(float3(light.xy-float2(0,1.23),3)))),24.0);
    colour=lerp(colour*float3(0.66,0.88,0.95),float3(0.46,0.76,0.94),0.09+0.62*crack+0.22*shine);
  }
  if(t>=1.2){
    float burst=t-1.2;
    [loop] for(int shard=0;shard<30;shard++){
      int wedge=shard/3, kind=shard%3;
      float seed=hash(float2(shard,73.0));
      float2 inner=crackCorner(wedge,0), next=crackCorner(wedge+1,0);
      float2 outer=crackCorner(wedge,1), following=crackCorner(wedge+1,1);
      float2 a=kind==0 ? float2(0,0) : inner;
      float2 b=kind==0 ? inner : kind==1 ? outer : following;
      float2 c=kind==0 ? next : kind==1 ? following : next;
      float2 mid=(a+b+c)/3.0;
      float3 centre=float3(mid.x,1.23+mid.y,0.1);
      centre+=float3((mid.x*0.75+seed*0.20-0.10)*burst,
        (0.45+seed*0.65+mid.y*0.5)*burst-1.6*burst*burst,
        (0.27+seed*1.20)*burst);
      float rest=centre.y<0.16 ? saturate((0.16-centre.y)*1.7) : 0.0;
      centre.y=max(centre.y,0.055+0.08*sin(rest*3.14159));
      float3 axis=normalize(float3(seed-0.5,0.35+seed,hash(float2(shard,17.0))-0.4));
      float angle=lerp(burst*(2.2+6.0*seed),1.56,rest);
      float3 va=centre+spin(float3(a-mid,0),axis,angle);
      float3 vb=centre+spin(float3(b-mid,0),axis,angle);
      float3 vc=centre+spin(float3(c-mid,0),axis,angle);
      float2 bary;
      float distance=hitTriangle(eye,ray,va,vb,vc,bary);
      if(distance<=0.1||distance>=depth)continue;
      float3 normal=normalize(cross(vb-va,vc-va));
      if(dot(ray,normal)>0.0)normal=-normal;
      float facing=abs(dot(ray,normal));
      float fresnel=0.04+0.96*pow(1.0-facing,5.0);
      float3 bounce=reflect(ray,normal);
      float gleam=pow(saturate(dot(bounce,normalize(light-centre))),28.0);
      float2 shift=normal.xy*(0.05+0.20*(1.0-facing));
      float3 refracted=brick((eye+wall*ray).xy+shift)*lighting;
      float edge=1.0-smoothstep(0.004,0.035,min(bary.x,min(bary.y,1.0-bary.x-bary.y)));
      float3 reflected=lerp(float3(0.15,0.26,0.38),float3(0.55,0.71,0.81),saturate(bounce.y*0.8+0.5));
      colour=lerp(refracted*float3(0.70,0.91,0.94),reflected,fresnel);
      colour+=float3(0.75,0.87,1.0)*(0.82*gleam+0.21*edge);
      depth=distance;
    }
  }
  return pow(max(colour,0.0),float3(0.78,0.78,0.78));
}
float4 psMain(float4 position : SV_Position) : SV_Target {
  float2 uv=(position.xy-float2(960,540))/1080.0;
  uv.y=-uv.y;
  if(scene>0.5)return float4(workshop(uv),1);
  float angle=0.60+0.47*sin(frame*0.30);
  float3 eye=float3(5.05*sin(angle),3.10,5.05*cos(angle));
  float3 aim=normalize(float3(0,0.15,0)-eye);
  float3 right=normalize(cross(aim,float3(0,1,0)));
  float3 up=cross(right,aim);
  float3 ray=normalize(aim+uv.x*right+uv.y*up);
  float d=0.0;
  float2 hit=float2(1.0,0.0);
  for(int i=0;i<56;i++){
    hit=world(eye+d*ray);
    if(hit.x<0.0025||d>18.0)break;
    d+=hit.x;
  }
  float3 sky=lerp(float3(0.11,0.23,0.39),float3(0.34,0.59,0.78),clamp(uv.y+0.5,0.0,1.0));
  float3 c=sky;
  if(hit.x<0.0025 && d<18.0){
    float3 p=eye+ray*d;
    float3 n=normal(p);
    float diffuse=max(dot(n,sun),0.0)*shadow(p+n*0.012);
    float shine=pow(max(dot(reflect(-sun,n),-ray),0.0),48.0);
    float gloss=(hit.y==4.0||hit.y==5.0||hit.y==6.0)?1.0:0.36;
    float rim=pow(1.0-saturate(dot(n,-ray)),4.0);
    float contact=0.70+0.30*smoothstep(0.0,0.25,p.y);
    c=colour(hit.y,p)*(0.44+0.71*diffuse)*contact+
       float3(0.32,0.46,0.65)*(gloss*shine+gloss*0.38*rim);
    c=lerp(c,sky,smoothstep(8.0,18.0,d));
  }
  return float4(pow(max(c,0.0),float3(0.86,0.86,0.86)),1.0);
}

float4 psHeavy(float4 position : SV_Position) : SV_Target {
  float3 total=0.0;
  [loop] for (int sampleIndex=0;sampleIndex<4;sampleIndex++) {
    float2 offset=float2(sampleIndex%2,sampleIndex/2)*0.5-0.25;
    float2 uv=(position.xy+offset-float2(1280,720))/1440.0;
    uv.y=-uv.y;
    total+=workshop(uv);
  }
  return float4(total*0.25,1.0);
}
