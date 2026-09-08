from pathlib import Path
import os,json,math
import numpy as np
from scipy.spatial.transform import Rotation
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
from matplotlib.colors import LightSource
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'generated';OUT.mkdir(exist_ok=True);R=75.
items=json.loads((ROOT/'parsed.json').read_text());areas=[i for i in items if i['collider'] and i['collider']['m_IsTrigger']];walls=[i for i in items if i['name'].startswith('Cube')]
for i in items:i['rot']=Rotation.from_quat(i['rotation']).as_matrix()
def unit(lon,lat):
 l,p=np.radians(lon),np.radians(lat);return np.stack([np.cos(p)*np.sin(l),np.sin(p),np.cos(p)*np.cos(l)],axis=-1)
def inside(p,i):
 q=(p-np.array(i['position']))@i['rot'];return np.all(np.abs(q-np.array([i['collider']['m_Center'][a]*s for a,s in zip('xyz',i['scale'])]))<=np.array(i['scale'])*np.array([i['collider']['m_Size'][a] for a in 'xyz'])/2,axis=-1)
def boundary(longitudes):
 # The nearest north/south collider surface along each meridian, measured on R75 sphere.
 lat=np.linspace(-60,70,13001);north=[];south=[];sources=[]
 for lo in longitudes:
  p=unit(np.full_like(lat,lo),lat)*R;nn=[];ss=[]
  for i in walls:
   hit=inside(p,i)
   if hit.any():
    pair=(float(lat[hit].min()),float(lat[hit].max()),i['name'])
    (nn if i['position'][1]>0 else ss).append(pair)
  assert nn and ss,lo
  ni=min(nn,key=lambda x:x[0]);si=max(ss,key=lambda x:x[1]);north.append(ni[0]);south.append(si[1]);sources.append(dict(longitude=float(lo),north_wall=ni[2],south_wall=si[2]))
 return np.array(north),np.array(south),sources
lons=np.linspace(20,50,121);nb,sb,sources=boundary(lons)
authored_sb=sb.copy()
# Keep the full A2 footprint clear. Its SE corner extends beyond the current southern wall.
a2source=next(i for i in areas if i['name']=='A2');lat_samples=np.linspace(-40,30,7001);floor=[];floor_lon=[]
for lo in lons:
 mask=inside(unit(np.full_like(lat_samples,lo),lat_samples)*75,a2source)
 if mask.any():floor.append(float(lat_samples[mask].min())-.75);floor_lon.append(float(lo))
for lo,low in zip(floor_lon,floor):sb=np.minimum(sb,low+4*np.abs(lons-lo))
max_south_adjustment=float(np.max(authored_sb-sb)*np.pi/180*R)

# Crossing two separate authored north walls can leave a step in the wall envelope.
# Keep the sampled envelope without smoothing it toward gameplay.
assert np.all(nb>sb)
# Terrain pieces are cross-sections in latitude. Heights are ORIGINAL PROPOSALS.
pieces={}
def add(name,lats,heights,color):
 L=np.broadcast_to(lons,(len(lats),len(lons)));T=np.array(lats);H=np.broadcast_to(np.asarray(heights),T.shape);V=unit(L,T)*(R+H[...,None] if H.ndim==3 else (R+H)[...,None]);faces=[];nr,nc=T.shape
 for j in range(nr-1):
  for i in range(nc-1):a=j*nc+i;faces.extend([(a,a+1,a+nc),(a+1,a+nc+1,a+nc)])
 V=V.reshape(-1,3);F=np.array(faces);norm=np.cross(V[F[:,1]]-V[F[:,0]],V[F[:,2]]-V[F[:,0]]);flip=np.einsum('ij,ij->i',norm,V[F[:,0]])<0;F[flip]=F[flip][:,[0,2,1]]
 pieces[name]=dict(V=V,F=F,color=color)
# Ground is a reference/replacement patch matching assumed spherical surface.
ts=np.linspace(0,1,37);add('A2_Playable_Reference',[sb+(nb-sb)*t for t in ts],0.,'#9dbe64')
# Ridge starts 0.3 degrees INTO the north wall; never closer to the playable band.
off=np.array([.0,.3,1.,2.5,5.,9.,14.,20.,26.]);ridge=[];rh=[]
for d in off:
 ridge.append(nb+d)
 base=np.interp(d,[0,.3,1.,2.5,5,9,14,20,26],[0,0,2.5,5,10,18,24,18,10])
 wave=(.5+.5*np.sin(np.radians(lons*23)))*(.8+.2*np.cos(np.radians(lons*41)))
 rh.append(base*(.90+.1*wave))
add('A2_North_Ridge',ridge,rh,'#77728e')
# Vertical-looking drop. Lower material must replace/clip the existing spherical ground.
off=np.array([0,.2,.5,1.,1.8,3.,6.,10.,16.,22.]);cliff=[];ch=[]
for d in off:
 cliff.append(sb-d);depth=np.interp(d,[0,.2,.5,1,1.8,3,6,10,16,22],[0,0,-1.5,-5,-9,-12,-12,-13,-14,-14])
 ch.append(np.full_like(lons,depth))
add('A2_South_Cliff',cliff,ch,'#665c7b')
# Save local-space meshes and a combined inspection mesh.
colors={name:p['color'] for name,p in pieces.items()}
with (OUT/'Nyxara-A2.mtl').open('w') as f:
 for name,col in colors.items():
  rgb=[int(col[a:a+2],16)/255 for a in [1,3,5]];f.write('newmtl '+name+'\nKd '+' '.join(map(str,rgb))+'\nKa 0.1 0.1 0.1\n\n')
def write_obj(path,selected):
 with path.open('w') as f:
  f.write('# PlanetNyxara parent-local coordinates. R=75. No colliders or runtime scripts.\nmtllib Nyxara-A2.mtl\n');offset=0
  for name in selected:
   p=pieces[name];f.write('o '+name+'\nusemtl '+name+'\n')
   for v in p['V']:f.write('v %.6f %.6f %.6f\n'%tuple(v))
   for tri in p['F']:f.write('f '+' '.join(str(int(i)+1+offset) for i in tri)+'\n')
   offset+=len(p['V'])
for name in pieces:write_obj(OUT/(name+'.obj'),[name])
write_obj(OUT/'A2_Terrain_Study.obj',list(pieces))
# Export exact authored cuboid references, to compare positioning in Unity/Blender.
verts=[];faces=[];names=[]
corners=np.array([[x,y,z] for x in [-.5,.5] for y in [-.5,.5] for z in [-.5,.5]])
quads=[[0,1,3,2],[4,6,7,5],[0,4,5,1],[2,3,7,6],[0,2,6,4],[1,5,7,3]]
with (OUT/'Authored_Blockout_Reference.obj').open('w') as f:
 for k,i in enumerate(walls+areas):
  vs=(corners*np.array(i['scale']))@i['rot'].T+np.array(i['position']);f.write('o '+i['name'].replace(' ','_')+'\n')
  for v in vs:f.write('v %.6f %.6f %.6f\n'%tuple(v))
  for face in quads:f.write('f '+' '.join(str(k*8+j+1) for j in face)+'\n')
# Plan projection for precise layout review.
fig,(ax,az)=plt.subplots(1,2,figsize=(15,7),facecolor='#10232d',gridspec_kw={'width_ratios':[1.1,1]})
for a in [ax,az]:a.set_facecolor('#10232d');a.tick_params(colors='white');a.grid(alpha=.15,color='white')
L,T=np.meshgrid(np.linspace(-180,180,900),np.linspace(-70,70,400));P=unit(L,T)*R
rgba=np.zeros((*L.shape,4));rgba[:]=[.17,.29,.26,1]
for i in walls:
 hit=inside(P,i);rgba[hit]=[.65,.67,.68,1]
for i in areas:
 hit=inside(P,i);rgba[hit]=[.3,.8,.8,1] if i['name'] in ['H','R1','R2'] else [.9,.52,.32,1] if i['name']!='B' else [.82,.29,.39,1]
ax.imshow(rgba,extent=[-180,180,-70,70],origin='lower',aspect='auto')
for i in areas:ax.text(i['lon'],i['lat'],i['name'],ha='center',va='center',color='white',weight='bold',bbox=dict(facecolor='#10232d',alpha=.7,edgecolor='none',pad=2))
ax.axvspan(20,50,color='#f7e5a1',alpha=.15);ax.set_title('YOUR PREFAB / RADIUS OVERRIDE 75',color='white',weight='bold',pad=15);ax.set_xlabel('Longitude in prefab parent coordinates',color='white');ax.set_ylabel('Latitude',color='white');ax.set_ylim(-65,65)
az.fill_between(lons,nb,nb+26,color='#77728e',alpha=.9);az.fill_between(lons,sb,nb,color='#9dbe64');az.fill_between(lons,sb-22,sb,color='#665c7b',alpha=.9);az.plot(lons,nb,'-',color='white',lw=1);az.plot(lons,sb,'-',color='white',lw=1);az.plot(lons,authored_sb,'--',color='#ffbd7c',lw=1.4)
# Exact A2 trigger/sphere intersection contour.
l,t=np.meshgrid(np.linspace(20,50,601),np.linspace(-45,65,1001));hit=inside(unit(l,t)*R,next(i for i in areas if i['name']=='A2'));az.contour(l,t,hit.astype(float),levels=[.5],colors=['#f8895b'],linewidths=2.5)
az.text(34.5,-5.25,'A2',ha='center',color='#14272e',weight='bold',fontsize=14);az.text(35,45,'NORTH RIDGE\npeak about +24 units',ha='center',color='white');az.text(35,-35,'SOUTH CLIFF\ndrop 12-14 units',ha='center',color='white');az.set_xlim(20,50);az.set_ylim(-50,65);az.set_xlabel('Longitude / study sector 20-50 degrees',color='white');az.set_title('A2 / PROPOSED TERRAIN SECTION',color='white',weight='bold',pad=15)
fig.text(.06,.02,'Gray = authored walls. Colored zone footprints = exact box/sphere intersections at assumed R75. Dashed orange = old south wall; new edge preserves A2. Height profiles are proposed.',color='white',fontsize=10);fig.tight_layout(rect=[0,.05,1,1]);fig.savefig(OUT/'Nyxara-A2-Plan.png',dpi=180);plt.close(fig)
# Real mesh preview: the same OBJ vertices, transformed to a local camera frame only for presentation.
a2=next(i for i in areas if i['name']=='A2');up=np.array(a2['position']);up/=np.linalg.norm(up);east=unit(a2['lon']+90,0);north=np.cross(up,east);origin=up*R
basis=np.array([east,north,up])
fig=plt.figure(figsize=(15,7),facecolor='#10232d')
for col,(elev,azim,label) in enumerate([(35,-70,'A2 / NORTH RIDGE + SOUTH DROP'),(12,-5,'SIDE / CURVED GROUND PRESERVED')]):
 ax=fig.add_subplot(1,2,col+1,projection='3d');ax.set_facecolor('#10232d')
 for name,p in pieces.items():
  V=(p['V']-origin)@basis.T;tri=V[p['F']];base=np.array([int(p['color'][k:k+2],16)/255 for k in [1,3,5]])
  normals=np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]);normals/=np.linalg.norm(normals,axis=1)[:,None];light=np.array([-.5,-.3,1]);light/=np.linalg.norm(light);shade=.65+.35*np.maximum(0,normals@light);fc=base[None,:]*shade[:,None]
  coll=Poly3DCollection(tri,facecolors=fc,edgecolors='none',zsort='average');ax.add_collection3d(coll)
 # A2 boundary contour on reference sphere.
 for cs in [None]:
  # Instead of a separate guessed square, scatter exact contour extraction from boolean mask.
  import scipy.ndimage as ndi
  boundary_hit=hit & ~ndi.binary_erosion(hit);pp=unit(l[boundary_hit],t[boundary_hit])*75.12;points=(pp-origin)@basis.T
  ax.scatter(points[::6,0],points[::6,1],points[::6,2],s=2,c='#f8895b',depthshade=False)
 pos=(up*(75.5)-origin)@basis.T;ax.text(*pos,' A2',color='white',weight='bold',fontsize=12)
 ax.set_xlim(-25,25);ax.set_ylim(-60,90);ax.set_zlim(-40,35);ax.set_box_aspect((50,150,75),zoom=1.4);ax.view_init(elev=elev,azim=azim);ax.axis('off');ax.set_title(label,color='white',fontsize=12,weight='bold',pad=14)
fig.text(.06,.04,'Measured: radius setting, box transforms, wall intersections. Proposed: mountain height and cliff depth. Open side edges are study-sector cuts.',color='white',fontsize=10);fig.subplots_adjust(left=0,right=1,bottom=.07,top=.93,wspace=0);fig.savefig(OUT/'Nyxara-A2-Mesh-Preview.png',dpi=180);plt.close(fig)
# Quantify fit by sampled terrain vertices and original collider envelope.
for name,p in pieces.items():assert np.isfinite(p['V']).all()
assert np.max(np.abs(np.linalg.norm(pieces['A2_Playable_Reference']['V'],axis=1)-75))<1e-10
clean=[{k:v for k,v in i.items() if k!='rot'} for i in items]
report=dict(source='PlanetNyxara.prefab',source_base_prefab_guid='aa06111e000000000000000000000006',missing_base_prefab=True,radius_override=75,coordinate_frame='Parent-local under PlanetNyxara; inherited root scale unresolved',objects=clean,study=dict(longitude_range=[20,50],north_boundary_latitude=nb.tolist(),south_boundary_latitude=sb.tolist(),authored_south_boundary_latitude=authored_sb.tolist(),max_south_adjustment_units=max_south_adjustment,longitude=lons.tolist(),wall_sources=sources,latitude_sampling_error_degrees=.01,max_radial_ridge_height=float(np.max(np.linalg.norm(pieces['A2_North_Ridge']['V'],axis=1)-75)),max_cliff_depth=14,triangle_count={k:len(p['F']) for k,p in pieces.items()}))
(OUT/'Measured-Layout.json').write_text(json.dumps(report,indent=2))
print(json.dumps(dict(area_count=len(areas),wall_count=len(walls),study_triangles=sum(len(p['F']) for p in pieces.values()),ground_radius_max_error=float(np.max(np.abs(np.linalg.norm(pieces['A2_Playable_Reference']['V'],axis=1)-75))),ridge_height=report['study']['max_radial_ridge_height'],south_adjustment=max_south_adjustment)))

# Unity importer payload: no OBJ handedness conversion required.
payload={'radius':75.0,'meshes':[]}
for name,p in pieces.items():
 col=p['color'];rgb=[int(col[a:a+2],16)/255 for a in [1,3,5]]
 payload['meshes'].append({'name':name,'vertices':[{'x':float(v[0]),'y':float(v[1]),'z':float(v[2])} for v in p['V']],'triangles':p['F'].reshape(-1).tolist(),'color':{'r':rgb[0],'g':rgb[1],'b':rgb[2],'a':1.}})
(OUT/'Nyxara-A2-MeshData.json').write_text(json.dumps(payload,separators=(',',':')))
