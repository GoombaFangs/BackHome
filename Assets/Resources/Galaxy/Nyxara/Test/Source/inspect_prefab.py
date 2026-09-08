import re,yaml,json,numpy as np
from pathlib import Path
import sys
p=Path(sys.argv[1])
s=p.read_text();docs={}
for kind,fid,body in re.findall(r'--- !u!(\d+) &(-?\d+)(?: stripped)?\n(.*?)(?=\n--- !u!|\Z)',s,re.S):docs[int(fid)]=yaml.safe_load(body)
gos={k:v['GameObject'] for k,v in docs.items() if 'GameObject' in v};trans={k:v['Transform'] for k,v in docs.items() if 'Transform' in v and 'm_GameObject' in v['Transform']}
items=[]
for tid,t in trans.items():
 g=gos[t['m_GameObject']['fileID']]; comps=[docs[c['component']['fileID']] for c in g['m_Component']]
 p=t['m_LocalPosition'];pv=np.array([p[a] for a in 'xyz']);r=np.linalg.norm(pv)
 items.append(dict(name=g['m_Name'],transform_id=tid,parent=t['m_Father']['fileID'],position=pv.tolist(),rotation=[t['m_LocalRotation'][a] for a in 'xyzw'],scale=[t['m_LocalScale'][a] for a in 'xyz'],radius=round(r,2),lon=round(float(np.degrees(np.arctan2(pv[0],pv[2]))),2),lat=round(float(np.degrees(np.arcsin(pv[1]/r))),2) if r else 0,collider=next((c['BoxCollider'] for c in comps if 'BoxCollider' in c),None)))
(Path(__file__).resolve().parent/'parsed.json').write_text(json.dumps(items,indent=2))
for i in items:print(i['name'],i['position'],i['scale'],'r=',i['radius'],'lon,lat=',i['lon'],i['lat'],'trigger=',None if i['collider'] is None else i['collider']['m_IsTrigger'])
