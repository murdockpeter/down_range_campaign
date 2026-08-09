let state;
let currentView = 'overview';
let selectedLocation = 'hill402';
let saveTimer;
let mapsReady = false;
let googleMap = null;
let mapMode = 'schematic';
let mapsLoadPromise = null;
let tacticalDrag = null;
let unityStatusData = null;

const $ = (selector) => document.querySelector(selector);
const esc = (value = '') => String(value).replace(/[&<>"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[char]));
const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
const momentumNames = { '-3': 'Crisis', '-2': 'Enemy initiative', '-1': 'Under pressure', 0: 'Contested', 1: 'Advantage', 2: 'Initiative', 3: 'Dominant' };
const viewNames = {
  overview: 'Operational picture', mission: 'Mission package', tactical: 'Tactical battle', mcpp: 'MCPP planning wizard', forces: 'Force status',
  logistics: 'Logistics & medical', intel: 'Intelligence estimate', aar: 'After action report',
  history: 'Campaign history', library: 'Offline field library'
};

function toast(message) {
  const el = $('#toast'); el.textContent = message; el.classList.add('show');
  setTimeout(() => el.classList.remove('show'), 2200);
}

function scheduleSave() {
  $('#saveState').textContent = 'SAVING…';
  clearTimeout(saveTimer);
  saveTimer = setTimeout(async () => {
    state.campaign.lastUpdated = new Date().toISOString();
    await window.campaignAPI.save(state);
    $('#saveState').textContent = 'SAVED LOCALLY';
  }, 350);
}

function updateHeader() {
  $('#viewEyebrow').textContent = viewNames[currentView].toUpperCase();
  $('#viewTitle').textContent = currentView === 'overview' ? state.campaign.name : viewNames[currentView];
  $('#turnValue').textContent = String(state.campaign.turn).padStart(2, '0');
  const date = new Date(`${state.campaign.date}T12:00:00`);
  $('#dateValue').textContent = date.toLocaleDateString('en-US', { day:'2-digit', month:'short', year:'numeric' }).toUpperCase();
}

function momentumClass(value) { return value > 0 ? 'good' : value < 0 ? 'bad' : 'warn'; }
function controlLegend() { return '<span class="info">● FRIENDLY</span> &nbsp; <span class="warn">● CONTESTED</span> &nbsp; <span class="bad">● ENEMY</span> &nbsp; <span>● UNKNOWN</span>'; }

function normalizeMapData() {
  const latviaLocations = {
    hill402:{name:'Nūmerne Heights',lat:56.842,lng:27.5,terrain:'Wooded lake country / low ridges'},
    radio:{name:'Grebņeva Relay',lat:56.856,lng:27.835,terrain:'Fictional relay compound / border approaches'},
    farm:{name:'Salnava Farmland',lat:56.81449,lng:27.55315,terrain:'Fields / woodland / drainage'},
    village:{name:'Kārsava',lat:56.78688,lng:27.68175,terrain:'Small town / rail and road approaches'},
    mine:{name:'Bozova Railhead',lat:56.81361,lng:27.69528,terrain:'Freight railway / scattered buildings'},
    highway:{name:'Malnava A13 Junction',lat:56.77333,lng:27.72333,terrain:'Highway / village edge / open fields'},
    crossing:{name:'Pudinava Dam',lat:56.71475,lng:27.7278,terrain:'Dam / wet ground / village approaches'},
    fob:{name:'FOB Mežvidi',lat:56.69995,lng:27.56455,terrain:'Fictional base / rural road network'}
  };
  const requiresLatviaMigration = Number(state.schemaVersion || 1) < 2 || Number(state.campaign.map?.center?.lat || 0) < 50;
  if(requiresLatviaMigration){
    const replacements=[['Karsin Valley','Kārsava–Malnava corridor'],['Karsin Village','Kārsava'],['Karsin Farm','Salnava farmland'],['Hill 402','Nūmerne Heights'],['Radio Site','Grebņeva Relay'],['radio site','Grebņeva Relay'],['Highway 7','A13'],['Old Mine','Bozova Railhead'],['FOB Resolute','FOB Mežvidi'],['Karsin River','A13 corridor']];
    let serialized=JSON.stringify(state);replacements.forEach(([from,to])=>{serialized=serialized.split(from).join(to);});state=JSON.parse(serialized);
    state.schemaVersion=2;state.campaign.theater='Kārsava–Malnava Corridor, Latgale, Latvia';
    state.campaign.map={center:{lat:56.785,lng:27.68},zoom:11,type:state.campaign.map?.type||'terrain'};
    state.locations.forEach(location=>Object.assign(location,latviaLocations[location.id]||{}));
    setTimeout(scheduleSave,0);
  }
  state.campaign.map ||= { center: { lat: 56.785, lng: 27.68 }, zoom: 11, type: 'terrain' };
  state.locations.forEach((location) => {
    location.lat ??= 42.212 - location.y * 0.00101;
    location.lng ??= 43.317 + location.x * 0.0021;
  });
}

function normalizePlanningData() {
  const m=state.mission;
  const defaults={
    missionNumber:m.number,currentStep:0,completed:[],
    problem:{currentState:'Enemy forces control the northern A13 corridor and use the Grebņeva Relay to support local command, sensing, and electronic warfare.',desiredState:'The relay disposition and approaches are understood without materially increasing enemy alert; BLUE retains the reconnaissance force.',problemStatement:'Task Force Lantern lacks the information needed to isolate the Grebņeva Relay while enemy observation, patrols, and civilian activity constrain reconnaissance.',specifiedTasks:'Reconnoiter the Nūmerne Heights and Grebņeva Relay. Avoid decisive engagement. Exfiltrate before civil traffic increases.',impliedTasks:'Infiltrate undetected; establish an OP; identify the antenna, security element, and access route; preserve the UAS; report; exfiltrate.',essentialTasks:'Observe the relay for two uninterrupted turns and return with at least 75% strength.',friendlyCog:'Small reconnaissance element linked to platoon command and UAS support.',enemyCog:'Relay-supported local warning and command network.',criticalVulnerability:'Reliance on exposed antennas, predictable patrol tracks, and clear fields of view around the compound.',assumptions:'The relay remains active through dawn. Civilian traffic begins near 0615L. The enemy QRF is not already at the compound.',limitations:'Do not deliberately engage civilians. Do not assault the compound. Complete by Turn 8.',ccirs:'Has BLUE been detected? Where is the QRF? Is the relay actively jamming or direction finding?',missionStatement:'At 0430L, a Task Force Lantern reconnaissance element infiltrates through Salnava farmland to observe the Grebņeva Relay, identify its security and access routes, and exfiltrate by Turn 8 in order to enable a follow-on raid without alerting the enemy.'},
    coas:[
      {name:'COA 1 — Wooded Ridge',concept:'Use the western woods and Nūmerne ridge for a deliberate concealed infiltration and long observation.',mainEffort:'Scout team establishes the ridge OP.',supportingEffort:'UAS confirms patrol gaps and relay activity.',reserve:'Radio operator remains one terrain feature behind the OP.',phases:'Infiltrate woods → establish OP → collect → exfiltrate through Salnava.',risk:'Slower movement may compress collection and exfiltration time.',feasible:true,acceptable:true,suitable:true,distinguishable:true,complete:true},
      {name:'COA 2 — Drainage Approach',concept:'Move quickly through farm drainage and central dead ground to a closer temporary OP.',mainEffort:'Scout team uses drainage ditches to close distance.',supportingEffort:'UAS screens the northern patrol track.',reserve:'No dedicated reserve; team remains concentrated.',phases:'Cross farm → bound through ditches → observe close → withdraw southwest.',risk:'Faster collection but greater exposure in central fields and to civilians.',feasible:true,acceptable:true,suitable:true,distinguishable:true,complete:true},
      {name:'COA 3 — Offset UAS',concept:'Keep the ground element concealed south of the ridge while launching the UAS from an offset position to collect against the relay.',mainEffort:'UAS operator conducts short, displaced collection windows from covered launch sites.',supportingEffort:'Scout team secures the launch area and establishes a fallback visual OP.',reserve:'Two-person scout pair remains prepared to recover the UAS or cover withdrawal.',phases:'Infiltrate Salnava edge → establish offset launch site → collect in short windows → recover and exfiltrate.',risk:'Reduces troop exposure but risks EW detection, loss of the UAS, and less reliable identification of ground security.',feasible:true,acceptable:true,suitable:true,distinguishable:true,complete:true}
    ],
    wargame:{technique:'Key events / sequence of essential tasks',enemyMostLikely:'Security patrol follows its normal track and investigates unusual movement only after confirmation.',enemyMostDangerous:'UAS or patrol detects BLUE early, relay transmits a report, and the QRF enters on the eastern road.',rows:[
      {event:'Infiltration',action:'BLUE moves from Salnava using cover and concealment.',reaction:'Patrol continues on the northern track.',civilian:'Early farm activity may expose movement near buildings.',counteraction:'Shift to woods; hold movement while civilians pass.'},
      {event:'Establish OP',action:'Scout team occupies Nūmerne ridge; UAS checks relay.',reaction:'Relay may detect emissions or visual UAS activity.',civilian:'No expected reaction away from farm.',counteraction:'Use brief UAS windows; displace OP if patrol turns west.'},
      {event:'Exfiltration',action:'BLUE withdraws by the selected covered route.',reaction:'If alerted, patrol fixes while QRF approaches from east.',civilian:'Civil traffic increases near the road and farm.',counteraction:'Break contact west; do not contest the paved road.'}
    ],branches:'If the western route is compromised, exfiltrate through the southern drainage line.',sequels:'Successful collection enables a later raid; confirmed alarm may instead generate a counter-reconnaissance or hasty defense mission.',decisionPoints:'Abort if the relay confirms detection, the QRF blocks exfiltration, or BLUE falls below 75% effective strength.',refinements:'Preserve at least one covered withdrawal route throughout observation.'},
    comparison:{criteria:[
      {name:'Avoid detection',weight:5,scores:[5,3,4]},{name:'Force preservation',weight:5,scores:[5,3,4]},{name:'Intelligence quality',weight:4,scores:[3,5,4]},{name:'Time available',weight:3,scores:[3,5,4]},{name:'Civilian impact',weight:4,scores:[5,2,5]}
    ],selected:0,rationale:'COA 1 best supports the campaign purpose by preserving the force and avoiding an alarm, even if observation is less detailed.'},
    orders:{situation:m.situation,mission:'At 0430L, the reconnaissance element observes the Grebņeva Relay and exfiltrates by Turn 8 in order to enable a follow-on raid without increasing enemy alert.',execution:`Commander's intent\nPurpose: ${m.intent.purpose}\nMethod: ${m.intent.method}\nEnd state: ${m.intent.endState}\n\nConcept: Execute the selected COA in three phases— infiltration, observation, and exfiltration.`,admin:'Carry only critical ammunition and one UAS. Casualties move with the team; serious casualties trigger abort unless movement remains possible. Account for all personnel and the UAS at exfiltration.',commandSignal:'Scout leader controls the mission. Radio operator succeeds command if required. Use radio only for compromise, casualty, objective complete, or abort. Primary signal: radio; alternate: prearranged visual signal; emergency: messenger.'},
    transition:{mapBrief:false,confirmationBrief:false,commsCheck:false,rehearsal:false,casualtyPlan:false,abortCriteria:false,uasPlan:false,questions:'',ready:false}
  };
  state.planning ||= {};
  state.planning.missionNumber ??= defaults.missionNumber;state.planning.currentStep ??= 0;state.planning.completed ||= [];
  for(const key of ['problem','wargame','comparison','orders','transition']) state.planning[key]={...defaults[key],...(state.planning[key]||{})};
  if(!Array.isArray(state.planning.coas)||!state.planning.coas.length)state.planning.coas=defaults.coas;
  while(state.planning.coas.length<3)state.planning.coas.push(structuredClone(defaults.coas[state.planning.coas.length]));
  if(!Array.isArray(state.planning.wargame.rows)||!state.planning.wargame.rows.length)state.planning.wargame.rows=defaults.wargame.rows;
  if(!Array.isArray(state.planning.comparison.criteria)||!state.planning.comparison.criteria.length)state.planning.comparison.criteria=defaults.comparison.criteria;
  state.planning.comparison.criteria.forEach((criterion,index)=>{criterion.scores||=[];while(criterion.scores.length<3)criterion.scores.push(defaults.comparison.criteria[index]?.scores[criterion.scores.length]??3);});
  if(Number(state.schemaVersion||1)<4){state.schemaVersion=4;setTimeout(scheduleSave,0);}
}

function normalizeTacticalData() {
  const weapon = (id,name,range,difficulty,damage,extra={}) => ({id,name,range,difficulty,damage:{sides:damage},...extra});
  const m4=weapon('m4','M4 carbine',36,3,6), m249=weapon('m249','M249 SAW',36,4,6,{fan:2}), ak=weapon('ak','AK rifle',28,3,6);
  const defaults = {
    scenario:'Silent Lantern', round:1, activeSide:'blue', actedSides:[], initiative:{blue:4,red:3,first:'blue'},
    selectedId:'b1', targetId:'r1', cover:'open', impairedMovement:false, alarm:false, observationTurns:0, completed:false, committed:false,
    log:[{id:'start',round:1,text:'BLUE wins initiative 4–3. Begin infiltration.',kind:'system'}],
    units:[
      {id:'b1',side:'blue',name:'Scout lead',role:'Team leader',forceId:'sq1',kind:'troop',x:12,y:82,move:8,skill:6,defense:4,status:'healthy',weapons:[m4],radio:true},
      {id:'b2',side:'blue',name:'Automatic rifleman',role:'Scout team',forceId:'sq1',kind:'troop',x:15,y:86,move:8,skill:6,defense:4,status:'healthy',weapons:[m249],radio:false},
      {id:'b3',side:'blue',name:'Scout rifleman',role:'Scout team',forceId:'sq1',kind:'troop',x:18,y:82,move:8,skill:6,defense:4,status:'healthy',weapons:[m4],radio:false},
      {id:'b4',side:'blue',name:'Scout medic',role:'Combat lifesaver',forceId:'sq1',kind:'troop',x:21,y:86,move:8,skill:6,medicalSkill:8,defense:4,status:'healthy',weapons:[m4],radio:false},
      {id:'b5',side:'blue',name:'Radio operator',role:'Platoon HQ',forceId:'hq',kind:'troop',x:9,y:87,move:8,skill:6,defense:4,status:'healthy',weapons:[m4],radio:true},
      {id:'b6',side:'blue',name:'Raven UAS',role:'Unarmed ISR',forceId:'uas',kind:'vehicle',x:13,y:90,move:48,skill:6,defense:3,status:'healthy',weapons:[],flying:true,radio:true},
      {id:'r1',side:'red',name:'Patrol leader',role:'Security patrol',kind:'troop',x:52,y:27,move:8,skill:6,defense:4,status:'healthy',weapons:[ak],radio:true},
      {id:'r2',side:'red',name:'Patrol rifleman',role:'Security patrol',kind:'troop',x:56,y:31,move:8,skill:6,defense:4,status:'healthy',weapons:[ak],radio:false},
      {id:'r3',side:'red',name:'North sentry',role:'Relay guard',kind:'troop',x:69,y:17,move:8,skill:6,defense:4,status:'healthy',weapons:[ak],radio:false},
      {id:'r4',side:'red',name:'Gate sentry',role:'Relay guard',kind:'troop',x:76,y:31,move:8,skill:6,defense:4,status:'healthy',weapons:[ak],radio:false},
      {id:'r5',side:'red',name:'EW operator',role:'Specialist',kind:'troop',x:73,y:21,move:8,skill:6,specialistSkill:8,defense:4,status:'healthy',weapons:[ak],radio:true,ew:true}
    ]
  };
  state.tactical ||= defaults;
  for (const [key,value] of Object.entries(defaults)) if (state.tactical[key] === undefined) state.tactical[key]=structuredClone(value);
  state.tactical.units.forEach(unit=>{unit.actionUsed??=false;unit.moved??=false;unit.reaction??=false;unit.focused??=false;unit.sprint??=false;unit.suppressed??=false;unit.weapons||=[];});
  if(Number(state.schemaVersion||1)<5){state.schemaVersion=5;setTimeout(scheduleSave,0);}
}

function renderMap() {
  const byId = Object.fromEntries(state.locations.map((location) => [location.id, location]));
  const links = state.links.map(([a,b]) => `<line class="map-link" x1="${byId[a].x}%" y1="${byId[a].y}%" x2="${byId[b].x}%" y2="${byId[b].y}%"/>`).join('');
  const nodes = state.locations.map((location) => `<g class="node ${location.control} ${location.id === selectedLocation ? 'selected' : ''}" data-location="${esc(location.id)}" transform="translate(${location.x * 8},${location.y * 5.25})"><circle class="inner" r="21"/><circle class="outer" r="13"/><circle fill="currentColor" r="4"/><text class="name" y="32">${esc(location.name)}</text><text class="meta" y="45">${esc(location.control)} · V${location.value}</text></g>`).join('');
  return `<svg class="map-svg" viewBox="0 0 800 525" preserveAspectRatio="xMidYMid meet">${links}${nodes}</svg>`;
}

function loadGoogleMaps(key) {
  if (mapsReady) return Promise.resolve();
  if (mapsLoadPromise) return mapsLoadPromise;
  mapsLoadPromise = new Promise((resolve, reject) => {
    let settled = false;
    const finish = (callback, value) => {
      if (settled) return;
      settled = true; clearTimeout(timer); callback(value);
    };
    globalThis.__downRangeMapsReady = () => { mapsReady = true; finish(resolve); };
    globalThis.gm_authFailure = () => finish(reject, new Error('Google rejected the key. Verify billing, Maps JavaScript API, and localhost restrictions.'));
    const script = document.createElement('script');
    script.id = 'google-maps-loader'; script.async = true;
    script.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(key)}&loading=async&v=weekly&callback=__downRangeMapsReady`;
    script.onerror = () => finish(reject, new Error('Google Maps could not be downloaded. Check the network connection.'));
    const timer = setTimeout(() => finish(reject, new Error('Google Maps did not initialize within 15 seconds.')), 15000);
    document.head.appendChild(script);
  }).catch((error) => { mapsLoadPromise = null; document.querySelector('#google-maps-loader')?.remove(); throw error; });
  return mapsLoadPromise;
}

function renderGoogleMap() {
  const target = $('#googleMap');
  if (!target || !mapsReady || !globalThis.google?.maps) return;
  const config = state.campaign.map;
  googleMap = new google.maps.Map(target, {
    center: config.center, zoom: config.zoom, mapTypeId: config.type || 'terrain',
    streetViewControl: false, fullscreenControl: true, mapTypeControl: false,
    clickableIcons: false, gestureHandling: 'greedy', backgroundColor: '#0b100e'
  });
  const colors = { friendly:'#69a9b5', contested:'#d6a34a', enemy:'#c45f50', unknown:'#77817c' };
  const locations = Object.fromEntries(state.locations.map(location => [location.id, location]));
  state.links.forEach(([a,b]) => new google.maps.Polyline({
    map: googleMap, path: [locations[a], locations[b]].map(p=>({lat:p.lat,lng:p.lng})),
    strokeColor:'#8d9d91', strokeOpacity:.75, strokeWeight:2,
    icons:[{icon:{path:'M 0,-1 0,1',strokeColor:'#8d9d91',strokeOpacity:.8,scale:2},offset:'0',repeat:'12px'}]
  }));
  const info = new google.maps.InfoWindow();
  state.locations.forEach((location) => {
    const marker = new google.maps.Marker({
      map: googleMap, position:{lat:location.lat,lng:location.lng}, title:location.name, draggable:true,
      label:{text:String(location.value),color:'#0b0e0c',fontSize:'10px',fontWeight:'800'},
      icon:{path:google.maps.SymbolPath.CIRCLE,fillColor:colors[location.control],fillOpacity:1,strokeColor:'#eef2ec',strokeWeight:2,scale:12}, zIndex:10
    });
    marker.addListener('click', () => {
      selectedLocation=location.id;
      info.setContent(`<div style="color:#17201c;max-width:250px"><b>${esc(location.name)}</b><br>${esc(location.control.toUpperCase())} · Value ${location.value}<hr><small>${esc(location.terrain)}<br>${esc(location.property)}</small></div>`);
      info.open({map:googleMap,anchor:marker});
    });
    marker.addListener('dragend', (event) => {
      const point=event.latLng.toJSON(); location.lat=point.lat; location.lng=point.lng; scheduleSave(); toast(`${location.name} repositioned`);
    });
  });
  googleMap.addListener('idle', () => {
    const center=googleMap.getCenter(); if(!center) return;
    config.center=center.toJSON(); config.zoom=googleMap.getZoom(); config.type=googleMap.getMapTypeId(); scheduleSave();
  });
}

async function showMapsSettings() {
  const existing = await window.campaignAPI.getMapsKey();
  $('#modalBody').innerHTML = `<div class="modal-pad"><span class="eyebrow">LOCAL CONFIGURATION</span><h2>Google Maps API key</h2><p style="color:var(--muted);font-size:12px;line-height:1.55">Use a browser key with Maps JavaScript API enabled. Restrict its website origin to <code>http://127.0.0.1:43118/*</code>. It will be encrypted with Windows protection and stored outside this project.</p><div class="form-field"><label>API key</label><input id="mapsKeyInput" type="password" autocomplete="off" placeholder="${existing?'Encrypted key is already saved':'Paste Demo API key'}"></div><div id="mapsMessage" style="margin-top:10px;color:var(--muted);font-size:11px"></div><div class="modal-actions"><button class="button danger" id="clearMapsKey" type="button">Clear key</button><button class="button" value="cancel">Cancel</button><button class="button primary" id="saveMapsKey" type="button">Save encrypted key</button></div></div>`;
  const modal=$('#modal'); modal.showModal();
  $('#clearMapsKey').onclick=async()=>{await window.campaignAPI.setMapsKey('');mapMode='schematic';modal.close();render();toast('Google Maps key cleared');};
  $('#saveMapsKey').onclick=async()=>{
    const key=$('#mapsKeyInput').value.trim();
    if(!key){$('#mapsMessage').textContent=existing?'Enter a replacement key or choose Clear key.':'Enter an API key.';return;}
    const result=await window.campaignAPI.setMapsKey(key);
    if(!result.saved){$('#mapsMessage').textContent=result.error;return;}
    $('#mapsMessage').textContent='Key encrypted. Loading Google Maps…';
    try{await loadGoogleMaps(key);mapMode='google';modal.close();render();toast('Google Maps enabled');}catch(error){$('#mapsMessage').textContent=error.message;}
  };
}

function overviewView() {
  const location = state.locations.find((item) => item.id === selectedLocation) || state.locations[0];
  const troops = state.forces.reduce((sum,item) => sum + item.current, 0);
  const authorized = state.forces.reduce((sum,item) => sum + item.authorized, 0);
  const readiness = Math.round(state.forces.reduce((sum,item) => sum + item.readiness, 0) / state.forces.length);
  const knownEnemy = state.locations.filter((item) => item.control === 'enemy').length;
  return `<div class="grid stats">
    <div class="card stat-card"><div class="stat-label">OPERATIONAL MOMENTUM</div><div class="stat-value ${momentumClass(state.campaign.momentum)}">${state.campaign.momentum > 0 ? '+' : ''}${state.campaign.momentum}</div><div class="stat-note">${momentumNames[state.campaign.momentum]}</div></div>
    <div class="card stat-card"><div class="stat-label">TASK FORCE STRENGTH</div><div class="stat-value">${troops}<small> / ${authorized}</small></div><div class="stat-note">${readiness}% average readiness</div></div>
    <div class="card stat-card"><div class="stat-label">ACTIVE MISSION</div><div class="stat-value good">#${String(state.mission.number).padStart(2,'0')}</div><div class="stat-note">${esc(state.mission.title)} · ${esc(state.mission.type)}</div></div>
    <div class="card stat-card"><div class="stat-label">ENEMY-HELD AREAS</div><div class="stat-value bad">${knownEnemy}</div><div class="stat-note">of ${state.locations.length} named areas</div></div>
  </div>
  <div class="grid two">
    <article class="card"><div class="card-header"><h2>${esc(state.campaign.theater)} · operational map</h2><div class="map-switch"><button class="button ${mapMode==='google'?'active':''}" id="googleMode">Google map</button>${mapMode==='google'&&mapsReady?`<button class="button" id="mapTypeToggle">${state.campaign.map.type==='satellite'?'Terrain':'Satellite'}</button>`:''}<button class="button ${mapMode==='schematic'?'active':''}" id="schematicMode">Schematic</button></div></div><div class="map-wrap">${mapMode==='google'?`<div id="googleMap" class="google-map"></div>${mapsReady?`<div class="map-legend-google">${controlLegend()} &nbsp; · &nbsp; DRAG MARKERS TO REPOSITION</div>`:`<div class="map-offline"><div><span class="eyebrow">GOOGLE MAPS OFFLINE</span><h3>Add your Maps JavaScript API key</h3><p>The encrypted key stays in this computer's application-data folder and is never written to the campaign or repository.</p><button class="button primary" id="configureMap">Configure key</button></div></div>`}`:renderMap()}</div></article>
    <div class="grid">
      <article class="card"><div class="card-header"><h2>Selected area</h2><span class="sub">VALUE ${location.value}</span></div><div class="card-body">
        <h2 style="margin:0 0 5px;font-weight:500">${esc(location.name)}</h2><p style="color:var(--muted);font:10px var(--mono);text-transform:uppercase">${esc(location.control)} · ${esc(location.terrain)}</p>
        <div class="kv"><b>Operational effect</b><span>${esc(location.property)}</span></div><div class="kv"><b>Current intelligence</b><span>${esc(location.intel)}</span></div>
        <button class="button" id="cycleControl" data-id="${esc(location.id)}">Change control</button>
      </div></article>
      <article class="card"><div class="card-header"><h2>Commander's intent</h2></div><div class="card-body">
        ${['purpose','method','endState'].map((key) => `<div class="intent-block"><b>${key === 'endState' ? 'End state' : key}</b><p>${esc(state.campaign.intent[key])}</p></div>`).join('')}
      </div></article>
      <article class="card"><div class="card-header"><h2>Situation</h2></div><div class="card-body"><div class="kv"><b>Campaign phase</b><span>${esc(state.campaign.phase)}</span></div><div class="kv"><b>Weather</b><span>${esc(state.campaign.weather)}</span></div><div class="kv"><b>Next decision</b><span>Approve Mission #${state.mission.number} force allocation and execute reconnaissance.</span></div></div></article>
    </div>
  </div>`;
}

function missionView() {
  const m = state.mission;
  return `<div class="mission-banner"><div><div class="kicker">MISSION ${String(m.number).padStart(2,'0')} · ${esc(m.type).toUpperCase()}</div><h2>OPERATION ${esc(m.title).toUpperCase()}</h2><p>${esc(m.time)} · ${esc(m.duration)} · ${esc(state.locations.find(x=>x.id===m.locationId)?.name || '')}</p></div><div class="status-tag">${esc(m.status)}</div></div>
  <div class="grid equal">
    <article class="card"><div class="card-header"><h2>Commander's intent</h2></div><div class="card-body">${['purpose','method','endState'].map(k=>`<div class="intent-block"><b>${k==='endState'?'End state':k}</b><p>${esc(m.intent[k])}</p></div>`).join('')}</div></article>
    <article class="card"><div class="card-header"><h2>Situation</h2></div><div class="card-body"><p style="margin:0;color:#c5cec7;font-size:13px;line-height:1.65">${esc(m.situation)}</p><div class="kv"><b>Force allocation</b><span>${m.allocation.map(esc).join('<br>')}</span></div></div></article>
    <article class="card"><div class="card-header"><h2>METT-TC</h2></div><div class="card-body">${Object.entries(m.metttc).map(([k,v])=>`<div class="kv"><b>${esc(k)}</b><span>${esc(v)}</span></div>`).join('')}</div></article>
    <article class="card"><div class="card-header"><h2>OCOKA</h2></div><div class="card-body">${Object.entries(m.ocoka).map(([k,v])=>`<div class="kv"><b>${esc(k)}</b><span>${esc(v)}</span></div>`).join('')}</div></article>
  </div>
  <article class="card" style="margin-top:16px"><div class="card-header"><h2>Objectives</h2><span class="sub">CHECK DURING PLAY</span></div><div class="card-body">${m.objectives.map(o=>`<label class="objective"><input type="checkbox" data-objective="${o.id}" ${o.complete?'checked':''}><span>${esc(o.text)}</span><small>+${o.points}</small></label>`).join('')}</div></article>
  <article class="card" style="margin-top:16px"><div class="card-header"><h2>Synchronization matrix</h2></div><div class="card-body" style="padding:0"><table><thead><tr><th>Phase</th><th>Who</th><th>What</th><th>When</th><th>Where</th><th>Trigger</th></tr></thead><tbody>${m.sync.map(r=>`<tr><td class="good">${esc(r.phase)}</td><td>${esc(r.who)}</td><td>${esc(r.what)}</td><td>${esc(r.when)}</td><td>${esc(r.where)}</td><td>${esc(r.trigger)}</td></tr>`).join('')}</tbody></table></div></article>`;
}

const mcppSteps=['Problem framing','COA development','COA war game','Comparison & decision','Orders development','Transition'];
const getPath=(object,path)=>path.split('.').reduce((value,key)=>value?.[key],object);
function setPath(object,path,value){const keys=path.split('.');const last=keys.pop();const target=keys.reduce((item,key)=>item[key],object);target[last]=value;}
function planField(label,path,{small=false,help=''}={}){const value=getPath(state.planning,path)||'';return `<div class="form-field ${small?'':'full'}"><label>${esc(label)}</label>${help?`<small>${esc(help)}</small>`:''}${small?`<input data-plan="${path}" value="${esc(value)}">`:`<textarea data-plan="${path}">${esc(value)}</textarea>`}</div>`;}
function planCheck(label,path){return `<label class="plan-check"><input type="checkbox" data-plan="${path}" ${getPath(state.planning,path)?'checked':''}><span>${esc(label)}</span></label>`;}

function problemStep(){return `<div class="form-grid">${planField('Current state','problem.currentState')}${planField('Desired state','problem.desiredState')}${planField('Problem statement','problem.problemStatement',{help:'What prevents movement from the current state to the desired state?'})}${planField('Specified tasks','problem.specifiedTasks')}${planField('Implied tasks','problem.impliedTasks')}${planField('Essential tasks','problem.essentialTasks')}${planField('Friendly center of gravity','problem.friendlyCog')}${planField('Enemy center of gravity','problem.enemyCog')}${planField('Critical vulnerability','problem.criticalVulnerability')}${planField('Assumptions','problem.assumptions',{help:'Logical, realistic, essential for planning, and must not assume away an adversary capability.'})}${planField('Limitations — restraints and constraints','problem.limitations')}${planField('Commander’s critical information requirements','problem.ccirs')}${planField('Mission statement — who, what, where, when, why','problem.missionStatement')}</div>`;}

function coaStep(){return `<div class="coa-grid">${state.planning.coas.map((coa,index)=>`<article class="coa-card"><div class="coa-number">COA ${index+1}</div>${planField('Name',`coas.${index}.name`,{small:true})}${planField('Concept / narrative',`coas.${index}.concept`)}${planField('Main effort',`coas.${index}.mainEffort`)}${planField('Supporting effort',`coas.${index}.supportingEffort`)}${planField('Reserve / security',`coas.${index}.reserve`)}${planField('Sequence and phases',`coas.${index}.phases`)}${planField('Primary risk',`coas.${index}.risk`)}<div class="coa-tests">${[['Feasible','feasible'],['Acceptable','acceptable'],['Suitable','suitable'],['Distinguishable','distinguishable'],['Complete','complete']].map(([label,key])=>planCheck(label,`coas.${index}.${key}`)).join('')}</div></article>`).join('')}</div>`;}

function wargameStep(){const w=state.planning.wargame;return `<div class="form-grid">${planField('War game technique','wargame.technique',{small:true})}${planField('Enemy most likely COA','wargame.enemyMostLikely')}${planField('Enemy most dangerous COA','wargame.enemyMostDangerous')}</div><div class="table-scroll"><table class="planning-table"><thead><tr><th>Critical event</th><th>Friendly action</th><th>Enemy reaction</th><th>Civilian reaction</th><th>Friendly counteraction</th></tr></thead><tbody>${w.rows.map((row,i)=>`<tr>${['event','action','reaction','civilian','counteraction'].map(key=>`<td><textarea data-plan="wargame.rows.${i}.${key}">${esc(row[key])}</textarea></td>`).join('')}</tr>`).join('')}</tbody></table></div><button class="button" id="addWargameRow">+ Critical event</button><div class="form-grid plan-followups">${planField('Branches','wargame.branches')}${planField('Sequels','wargame.sequels')}${planField('Decision points / abort criteria','wargame.decisionPoints')}${planField('Plan refinements','wargame.refinements')}</div>`;}

function coaTotal(coaIndex){return state.planning.comparison.criteria.reduce((sum,item)=>sum+Number(item.weight||0)*Number(item.scores?.[coaIndex]||0),0);}
function comparisonStep(){const c=state.planning.comparison;return `<div class="table-scroll"><table class="planning-table score-table"><thead><tr><th>Evaluation criterion</th><th>Weight</th>${state.planning.coas.map((coa,i)=>`<th>${esc(coa.name||`COA ${i+1}`)} score</th>`).join('')}</tr></thead><tbody>${c.criteria.map((item,i)=>`<tr><td><input data-plan="comparison.criteria.${i}.name" value="${esc(item.name)}"></td><td><input type="number" min="1" max="5" data-plan="comparison.criteria.${i}.weight" value="${item.weight}"></td>${state.planning.coas.map((_coa,j)=>`<td><input type="number" min="1" max="5" data-plan="comparison.criteria.${i}.scores.${j}" value="${item.scores[j]}"></td>`).join('')}</tr>`).join('')}<tr class="score-total"><td>WEIGHTED TOTAL</td><td>—</td>${state.planning.coas.map((_coa,i)=>`<td>${coaTotal(i)}</td>`).join('')}</tr></tbody></table></div><div class="decision-grid">${state.planning.coas.map((coa,i)=>`<label class="decision-card ${Number(c.selected)===i?'selected':''}"><input type="radio" name="selectedCoa" data-plan="comparison.selected" value="${i}" ${Number(c.selected)===i?'checked':''}><span><small>SELECT</small><b>${esc(coa.name)}</b><strong>${coaTotal(i)} pts</strong></span></label>`).join('')}</div><div class="form-grid">${planField('Commander’s decision and rationale','comparison.rationale')}</div>`;}

function ordersStep(){return `<div class="order-tabs"><span>1 · Situation</span><span>2 · Mission</span><span>3 · Execution</span><span>4 · Admin & logistics</span><span>5 · Command & signal</span></div><div class="form-grid">${planField('1. Situation','orders.situation')}${planField('2. Mission','orders.mission')}${planField('3. Execution — intent, CONOPS, tasks, coordinating instructions','orders.execution')}${planField('4. Administration and logistics','orders.admin')}${planField('5. Command and signal','orders.commandSignal')}</div><div class="doctrine-note">A good order is judged by usefulness, not size. Reconcile internal details, then crosswalk against the campaign intent, adjacent elements, force ledger, and mission objectives.</div>`;}

function transitionStep(){const t=state.planning.transition;const checks=[['Map / terrain-model brief','mapBrief'],['Subordinate confirmation brief','confirmationBrief'],['Communications exercise','commsCheck'],['Rehearsal of concept','rehearsal'],['Casualty movement plan rehearsed','casualtyPlan'],['Abort criteria understood','abortCriteria'],['UAS launch and recovery rehearsed','uasPlan']];return `<div class="transition-grid"><div><h3>Transition events</h3>${checks.map(([label,key])=>planCheck(label,`transition.${key}`)).join('')}</div><div><h3>Ready-to-execute standard</h3><p>The player can explain the intent, selected COA, key decision points, RED reactions, casualty plan, and signals without reopening the plan.</p>${planField('Questions, friction, or final changes','transition.questions')}<label class="ready-toggle ${t.ready?'ready':''}"><input type="checkbox" data-plan="transition.ready" ${t.ready?'checked':''}><span>${t.ready?'READY TO EXECUTE':'NOT YET READY'}</span></label></div></div>`;}

function mcppView(){const p=state.planning;const step=clamp(Number(p.currentStep||0),0,5);const renderers=[problemStep,coaStep,wargameStep,comparisonStep,ordersStep,transitionStep];const descriptions=['Understand the environment, problem, tasks, centers of gravity, limitations, and mission.','Develop distinguishable options and test each for feasibility, acceptability, suitability, distinctness, and completeness.','Improve each COA against a thinking enemy and civilian reactions using action–reaction–counteraction.','Evaluate each COA against commander-selected criteria, compare relative merit, and decide.','Translate the decision into clear direction sufficient for execution and initiative.','Transfer understanding through briefs, checks, and rehearsals until the force is ready to execute.'];return `<div class="mcpp-head"><div><span class="eyebrow">MCWP 5-10 · SIX-STEP PROCESS</span><h2>Mission ${p.missionNumber}: ${esc(state.mission.title)}</h2><p>${esc(descriptions[step])}</p></div><div><button class="button planning-ref" data-file="MCPP-References.pdf">Pocket guide</button> <button class="button planning-ref" data-file="MCWP 5-10 (1) (SECURED).pdf">MCWP 5-10</button></div></div><div class="mcpp-layout"><aside class="mcpp-steps">${mcppSteps.map((name,i)=>`<button class="mcpp-step ${i===step?'active':''} ${p.completed.includes(i)?'complete':''}" data-mcpp-step="${i}"><i>${p.completed.includes(i)?'✓':i+1}</i><span>${esc(name)}</span></button>`).join('')}<div class="mcpp-progress"><span>${p.completed.length}/6 COMPLETE</span><div class="bar"><i style="width:${p.completed.length/6*100}%"></i></div></div></aside><article class="card mcpp-work"><div class="card-header"><h2>Step ${step+1} · ${mcppSteps[step]}</h2><span class="sub">AUTOSAVES LOCALLY</span></div><div class="card-body">${renderers[step]()}</div><footer class="wizard-actions"><button class="button" id="mcppBack" ${step===0?'disabled':''}>← Back</button><button class="button ${p.completed.includes(step)?'good':''}" id="toggleStepComplete">${p.completed.includes(step)?'✓ Step complete':'Mark step complete'}</button><button class="button primary" id="mcppNext" ${step===5?'disabled':''}>Next →</button></footer></article></div>`;}

function forcesView() {
  return `<div class="toolbar"><input id="forceSearch" class="search" placeholder="Filter task force…"><div class="toolbar-actions"><button class="button primary" id="addForce">+ Add element</button></div></div>
  <article class="card"><div class="card-header"><h2>Task Force Lantern · persistent order of battle</h2><span class="sub">CLICK A ROW TO EDIT</span></div><div class="card-body" style="padding:0"><table><thead><tr><th>Element</th><th>Type</th><th>Strength</th><th>Readiness</th><th>Critical ammo</th><th>Special assets</th><th>Status</th></tr></thead><tbody id="forceRows">${forceRows(state.forces)}</tbody></table></div></article>`;
}
function forceRows(items) { return items.map(f=>`<tr class="force-row" data-id="${f.id}"><td><b>${esc(f.name)}</b></td><td>${esc(f.type)}</td><td>${f.current} / ${f.authorized}</td><td><span class="${f.readiness<60?'bad':f.readiness<85?'warn':'good'}">${f.readiness}%</span><div class="bar"><i style="width:${f.readiness}%"></i></div></td><td>${esc(f.ammo)}</td><td>${esc(f.assets)}</td><td>${esc(f.status)}</td></tr>`).join(''); }

function logisticsView() {
  return `<div class="grid equal"><article class="card"><div class="card-header"><h2>Critical supply ledger</h2><span class="sub">TRACK SIGNIFICANT ITEMS ONLY</span></div><div class="card-body"><div class="supply-grid">${state.supply.map(s=>`<div class="supply-item"><div class="supply-head"><span>${esc(s.name)}</span><strong class="${s.current/s.max<.35?'bad':s.current/s.max<.65?'warn':'good'}">${s.current}/${s.max}</strong></div><div class="bar"><i style="width:${clamp(s.current/s.max*100,0,100)}%"></i></div><div style="display:flex;gap:6px;margin-top:12px"><button class="icon-button supply-change" data-id="${s.id}" data-delta="-1">−</button><button class="icon-button supply-change" data-id="${s.id}" data-delta="1">+</button><small style="margin-left:auto;color:var(--muted);align-self:center">${esc(s.unit)}</small></div></div>`).join('')}</div></div></article>
  <article class="card"><div class="card-header"><h2>Casualty & return-to-duty ledger</h2><button class="button" id="addCasualty">+ Record casualty</button></div><div class="card-body" style="padding:0">${state.casualties.length?`<table><thead><tr><th>Personnel</th><th>Unit</th><th>Category</th><th>RTD turn</th><th>Notes</th></tr></thead><tbody>${state.casualties.map(c=>`<tr><td>${esc(c.name)}</td><td>${esc(c.unit)}</td><td class="${c.category==='KIA'?'bad':c.category==='WIA-S'?'warn':'good'}">${esc(c.category)}</td><td>${c.returnTurn ?? '—'}</td><td>${esc(c.note)}</td></tr>`).join('')}</tbody></table>`:'<div class="empty">No campaign casualties recorded.</div>'}</div></article></div>
  <article class="card" style="margin-top:16px"><div class="card-header"><h2>Campaign casualty model</h2></div><div class="card-body"><div class="grid stats" style="margin:0"><div><span class="field-label">KIA</span><p>Permanently removed</p></div><div><span class="field-label">WIA-L</span><p>Returns next mission</p></div><div><span class="field-label">WIA-S</span><p>Misses 1–3 missions</p></div><div><span class="field-label">RTD</span><p>Returned during battle</p></div></div></div></article>`;
}

function intelView() {
  return `<div class="toolbar"><div style="color:var(--muted);font-size:12px">BLUE sees assessed information; uncertainty remains part of the campaign.</div><button class="button primary" id="addIntel">+ Add report</button></div><article class="card"><div class="card-header"><h2>Intelligence holdings</h2><span class="sub">SOURCE RELIABILITY / INFORMATION CREDIBILITY</span></div><div>${state.intel.map(i=>`<div class="intel-card"><div class="intel-grade">${esc(i.grade)}</div><div><h3>${esc(i.title)}</h3><p>${esc(i.detail)}</p></div><div class="intel-meta">${esc(i.confidence)} confidence<br>${esc(i.age)} old</div></div>`).join('')}</div></article>
  <div class="grid equal" style="margin-top:16px"><article class="card"><div class="card-header"><h2>Known enemy capabilities</h2></div><div class="card-body"><div class="kv"><b>Maneuver</b><span>Platoon-sized element; possible armored QRF</span></div><div class="kv"><b>ISR</b><span>Small UAS operating around the A13 corridor</span></div><div class="kv"><b>EW</b><span>Jamming and direction finding assessed at Grebņeva Relay</span></div><div class="kv"><b>Fires</b><span>Unknown; mortar support remains plausible</span></div></div></article><article class="card"><div class="card-header"><h2>Priority intelligence requirements</h2></div><div class="card-body"><div class="objective"><span>01</span><span>What protects the Grebņeva Relay?</span><small>OPEN</small></div><div class="objective"><span>02</span><span>Where is the enemy QRF based?</span><small>OPEN</small></div><div class="objective"><span>03</span><span>Can Bozova Railhead support enemy logistics?</span><small>OPEN</small></div></div></article></div>`;
}

function tacticalUnit(id){return state.tactical.units.find(unit=>unit.id===id);}
function tacticalLiving(side){return state.tactical.units.filter(unit=>unit.side===side&&!['dead'].includes(unit.status));}
function tacticalLog(text,kind='system'){
  state.tactical.log.unshift({id:`tl${Date.now()}${Math.random()}`,round:state.tactical.round,text,kind});
  state.tactical.log=state.tactical.log.slice(0,80);
}
function diceText(roll){return roll.rolls.length>1?`[${roll.rolls.join(', ')}] → ${roll.result}`:`${roll.result}`;}
function tacticalToken(unit){
  const selected=unit.id===state.tactical.selectedId?'selected':'';
  const target=unit.id===state.tactical.targetId?'targeted':'';
  const flags=[unit.suppressed?'S':'',unit.reaction?'R':'',unit.focused?'F':'',unit.status==='injured'?'W':'',unit.status==='downed'?'↓':''].filter(Boolean).join('');
  return `<button class="tactical-token ${unit.side} ${selected} ${target} ${unit.status}" data-tactical-unit="${unit.id}" style="left:${unit.x}%;top:${unit.y}%" title="${esc(unit.name)}"><span>${unit.kind==='vehicle'?'U':unit.side==='blue'?'B':'R'}</span>${flags?`<i>${flags}</i>`:''}<b>${esc(unit.name)}</b></button>`;
}
function tacticalView(){
  const t=state.tactical, unit=tacticalUnit(t.selectedId)||t.units[0], target=tacticalUnit(t.targetId);
  const unityPending=state.unityBattle?.pendingRequestId;
  const range=target&&unit?TacticalRules.distanceInches(unit,target):0;
  const canAct=unit&&unit.status!=='downed'&&unit.status!=='dead'&&((unit.side===t.activeSide&&!unit.actionUsed)||unit.reaction);
  const weapon=unit?.weapons?.[0];
  const statusCount=side=>({ready:tacticalLiving(side).filter(u=>u.status==='healthy'||u.status==='injured').length,down:t.units.filter(u=>u.side===side&&u.status==='downed').length,lost:t.units.filter(u=>u.side===side&&u.status==='dead').length});
  const blue=statusCount('blue'),red=statusCount('red');
  return `<div class="tactical-shell">
    <header class="tactical-command"><div><span class="eyebrow">DOWN RANGE v1.4.2 · ${esc(t.scenario)}</span><strong>ROUND ${t.round} · <em class="${t.activeSide}">${t.activeSide.toUpperCase()} TURN</em></strong></div><div class="initiative"><span>UNITY RESOLVER</span><b class="${unityStatusData?.playerInstalled?'good':'warn'}">${unityStatusData?.playerInstalled?'READY':'NOT BUILT'}</b>${unityPending?'<b class="info">BATTLE ACTIVE</b>':''}</div><div class="tactical-command-actions"><button class="button primary" id="launchUnityBattle" ${unityStatusData?.playerInstalled&&!t.committed?'':'disabled'}>${unityPending?'Resume Unity':'Launch Unity'}</button><button class="button" id="launchUnityOneStar" ${unityStatusData?.playerInstalled?'':'disabled'}>One Star 3D</button><button class="button" id="importUnityBattle" ${unityPending?'':'disabled'}>Import result</button><button class="button" id="tacticalRules">Rules PDF</button><button class="button" id="resetTactical">Reset battle</button><button class="button" id="endTacticalTurn">End ${t.activeSide} turn</button></div></header>
    <div class="tactical-grid">
      <aside class="tactical-roster"><div class="tactical-panel-title">ORDER OF BATTLE</div>${['blue','red'].map(side=>`<section><h3 class="${side}">${side.toUpperCase()} <small>${statusCount(side).ready} effective</small></h3>${t.units.filter(u=>u.side===side).map(u=>`<button class="roster-unit ${u.id===unit?.id?'active':''} ${u.status}" data-roster-unit="${u.id}"><i></i><span><b>${esc(u.name)}</b><small>${esc(u.role)} · ${u.status}${u.actionUsed?' · acted':''}</small></span></button>`).join('')}</section>`).join('')}</aside>
      <main class="tactical-stage"><div class="tactical-board" id="tacticalBoard"><img src="assets/maps/silent-lantern-tts-map-v1.png" alt="Silent Lantern tactical map" draggable="false"><div class="deployment-zone blue">BLUE ENTRY</div><div class="relay-zone">RELAY OBSERVATION ZONE</div>${t.units.map(tacticalToken).join('')}${unit&&target?`<svg class="range-line"><line x1="${unit.x}%" y1="${unit.y}%" x2="${target.x}%" y2="${target.y}%"/><text x="${(unit.x+target.x)/2}%" y="${(unit.y+target.y)/2}%">${range.toFixed(1)}\"</text></svg>`:''}</div><div class="board-scale"><span>BOARD 64\" × 42.7\" · DRAG ACTIVE UNITS TO MOVE</span><span>1\" = 24 MAP PX</span></div></main>
      <aside class="tactical-inspector"><div class="tactical-panel-title">UNIT CONTROL</div>${unit?`<div class="unit-card-head ${unit.side}"><span>${unit.kind==='vehicle'?'UAS':unit.side.toUpperCase()}</span><h2>${esc(unit.name)}</h2><p>${esc(unit.role)}</p></div><div class="unit-stats"><div><span>MOVE</span><b>${TacticalRules.movementAllowance(unit,{sprint:unit.sprint,impaired:t.impairedMovement}).toFixed(0)}\"</b></div><div><span>SKILL</span><b>d${unit.skill}</b></div><div><span>DEF</span><b>${unit.defenseDice?`${unit.defenseDice.count}d${unit.defenseDice.sides}`:unit.defense}</b></div></div><div class="status-chips">${unit.suppressed?'<span>SUPPRESSED</span>':''}${unit.reaction?'<span>REACTION</span>':''}${unit.focused?'<span>FOCUSED</span>':''}${unit.status==='injured'?'<span>INJURED</span>':''}</div><div class="action-block"><label>WEAPON</label><select id="tacticalWeapon" ${!unit.weapons.length?'disabled':''}>${unit.weapons.length?unit.weapons.map((w,i)=>`<option value="${i}">${esc(w.name)} · ${w.range}\" · D${w.difficulty}</option>`).join(''):'<option>Unarmed</option>'}</select><label>TARGET / LOS <small>${target?`${esc(target.name)} · ${range.toFixed(1)}\"`:'Select an opposing token'}</small></label><select id="tacticalCover"><option value="open" ${t.cover==='open'?'selected':''}>Open / clear line of sight</option><option value="partial" ${t.cover==='partial'?'selected':''}>Partial cover or concealment</option><option value="blocked" ${t.cover==='blocked'?'selected':''}>Total cover / no line of sight</option></select><label>MOVEMENT TERRAIN</label><select id="tacticalTerrain"><option value="normal" ${!t.impairedMovement?'selected':''}>Normal movement</option><option value="impaired" ${t.impairedMovement?'selected':''}>Mud / climb / crawl — half speed</option></select><div class="action-buttons"><button class="button primary" id="tacticalFire" ${!canAct||!weapon||!target?'disabled':''}>Fire</button><button class="button" id="tacticalSuppress" ${!canAct||!weapon||!target?'disabled':''}>Suppress</button><button class="button" id="tacticalReaction" ${!canAct||unit.reaction?'disabled':''}>Hold reaction</button><button class="button" id="tacticalSprint" ${!canAct||unit.kind!=='troop'||unit.sprint?'disabled':''}>Sprint</button><button class="button" id="tacticalFocus" ${!canAct?'disabled':''}>Focus</button><button class="button" id="tacticalObserve" ${!canAct||!unit.radio||!target?'disabled':''}>Radio observe</button><button class="button" id="tacticalTreat" ${!canAct||!target||target.side!==unit.side||target.status!=='downed'?'disabled':''}>Treat casualty</button><button class="button" id="observeRelay" ${!canAct||unit.side!=='blue'?'disabled':''}>Observe relay</button></div></div>`:''}<div class="rules-reminder"><b>ACTIVATION</b><p>Each unit has one movement and one action, in either order. Focus gives up both. A reaction interrupts its trigger.</p></div></aside>
    </div>
    <footer class="tactical-footer"><div class="mission-tracker"><span class="${t.observationTurns>=2?'complete':''}"><b>${t.observationTurns}/2</b> RELAY OBSERVATION</span><span class="${t.alarm?'failed':''}"><b>${t.alarm?'RAISED':'CLEAR'}</b> ALARM</span><span><b>${blue.ready}/${t.units.filter(u=>u.side==='blue').length}</b> BLUE EFFECTIVE</span><span><b>${red.ready}/${t.units.filter(u=>u.side==='red').length}</b> RED EFFECTIVE</span><button class="button ${t.completed?'primary':''}" id="finishTactical">${t.committed?'RESULTS COMMITTED':t.completed?'Commit results to campaign':'End mission'}</button></div><div class="combat-log"><b>COMBAT LOG</b>${t.log.slice(0,5).map(item=>`<p class="${item.kind}"><span>R${item.round}</span>${esc(item.text)}</p>`).join('')}</div></footer>
  </div>`;
}

function tacticalCanUseAction(unit){return unit&&unit.status!=='downed'&&unit.status!=='dead'&&((unit.side===state.tactical.activeSide&&!unit.actionUsed)||unit.reaction);}
function consumeTacticalAction(unit){if(unit.reaction){unit.reaction=false;}else unit.actionUsed=true;}
function executeTacticalAttack(suppress=false){
  const t=state.tactical,attacker=tacticalUnit(t.selectedId),target=tacticalUnit(t.targetId),weapon=attacker?.weapons?.[Number($('#tacticalWeapon')?.value||0)];
  if(!tacticalCanUseAction(attacker))return toast('That unit has no action available.');
  const range=TacticalRules.distanceInches(attacker,target);
  const advantage=target?.observedBy===attacker.side&&target?.observedRound===t.round?1:0;
  const result=TacticalRules.resolveAttack({attacker,target,weapon,range,advantage,cover:t.cover,suppress});
  if(!result.ok){toast(result.reason);return;}
  consumeTacticalAction(attacker);t.alarm=true;
  const skill=`skill ${diceText(result.skill)} vs ${result.difficulty}`;
  if(!result.hit)tacticalLog(`${attacker.name} misses ${target.name} (${skill}).`,'miss');
  else if(suppress){target.suppressed=true;target.suppressedBySide=attacker.side;tacticalLog(`${attacker.name} suppresses ${target.name} (${skill}).`,'suppress');}
  else if(result.casualty){target.status='downed';target.reaction=false;tacticalLog(`${attacker.name} downs ${target.name} (${skill}; damage ${diceText(result.damage)} vs defense ${result.defense.total}).`,'hit');}
  else tacticalLog(`${attacker.name} hits ${target.name}, but causes no casualty (${skill}; damage ${diceText(result.damage)} vs defense ${result.defense.total}).`,'hit');
  scheduleSave();render();
}
function startTacticalSide(side){
  const t=state.tactical;t.activeSide=side;
  t.units.filter(u=>u.side===side).forEach(u=>{u.actionUsed=false;u.moved=false;u.focused=false;u.sprint=false;u.reaction=false;});
  t.units.filter(u=>u.suppressedBySide===side).forEach(u=>{u.suppressed=false;u.suppressedBySide=null;});
  tacticalLog(`${side.toUpperCase()} turn begins.`);
}
function endTacticalTurn(){
  const t=state.tactical,side=t.activeSide;t.actedSides.push(side);
  t.units.filter(u=>u.side===side).forEach(u=>u.reaction ||= !u.actionUsed&&u.status==='healthy');
  if(t.actedSides.length===1)startTacticalSide(side==='blue'?'red':'blue');
  else{t.round+=1;t.actedSides=[];t.initiative=TacticalRules.rollInitiative();startTacticalSide(t.initiative.first);tacticalLog(`Initiative: BLUE ${t.initiative.blue}, RED ${t.initiative.red}. ${t.initiative.first.toUpperCase()} acts first.`);}
  scheduleSave();render();
}
function executeMedicine(){
  const medic=tacticalUnit(state.tactical.selectedId),target=tacticalUnit(state.tactical.targetId);
  if(!tacticalCanUseAction(medic)||!target||target.side!==medic.side||target.status!=='downed')return;
  const range=TacticalRules.distanceInches(medic,target);if(range>1.5)return toast(`Move adjacent first (${range.toFixed(1)}\" away).`);
  medic.actionUsed=true;medic.focused=true;medic.moved=true;
  const result=TacticalRules.resolveMedicine(medic.medicalSkill||medic.skill);target.status=result.result==='no-effect'?'downed':result.result;
  tacticalLog(`${medic.name} treats ${target.name}: ${diceText(result.roll)} — ${result.result.replace('-',' ')}.`,'medical');scheduleSave();render();
}
function observeRelay(){
  const unit=tacticalUnit(state.tactical.selectedId);if(!tacticalCanUseAction(unit))return;
  const relay={x:73,y:22};const range=TacticalRules.distanceInches(unit,relay);if(range>18)return toast(`Move within 18\" of the relay (${range.toFixed(1)}\" now).`);
  consumeTacticalAction(unit);state.tactical.observationTurns=clamp(state.tactical.observationTurns+1,0,2);
  tacticalLog(`${unit.name} completes an uninterrupted relay observation (${state.tactical.observationTurns}/2).`,'objective');scheduleSave();render();
}
function finishTacticalMission(){
  const t=state.tactical;
  if(!t.completed){t.completed=true;tacticalLog('Mission ended. Review the board, then commit results to the campaign.','objective');scheduleSave();render();return;}
  if(t.committed)return;
  const blue=t.units.filter(u=>u.side==='blue'),effective=blue.filter(u=>!['downed','dead'].includes(u.status)).length;
  state.mission.objectives.find(o=>o.id==='o1').complete=t.observationTurns>=2;
  state.mission.objectives.find(o=>o.id==='o2').complete=t.observationTurns>=2;
  state.mission.objectives.find(o=>o.id==='o3').complete=effective/blue.length>=.75;
  state.mission.objectives.find(o=>o.id==='o4').complete=!t.alarm;
  const casualties=blue.filter(u=>['downed','dead'].includes(u.status));
  casualties.forEach((u,index)=>{const force=state.forces.find(f=>f.id===u.forceId);if(force)force.current=Math.max(0,force.current-1);state.casualties.push({id:`tc${Date.now()}${index}`,name:u.name,unit:force?.name||u.role,category:u.status==='dead'?'KIA':'WIA-S',returnTurn:u.status==='dead'?null:state.campaign.turn+2,note:`Tactical casualty, Mission ${state.mission.number}: ${state.mission.title}`});});
  t.committed=true;state.mission.status='awaiting-aar';state.mission.tacticalSummary={rounds:t.round,alarm:t.alarm,observationTurns:t.observationTurns,kia:casualties.filter(u=>u.status==='dead').length,serious:casualties.filter(u=>u.status==='downed').length,effective,starting:blue.length,log:t.log.slice().reverse().map(x=>`R${x.round}: ${x.text}`).join('\n')};
  scheduleSave();render();toast('Tactical results committed · AAR is ready');
}

function aarView() {
  const done=state.mission.status==='complete';
  if(done)return `<article class="card"><div class="card-header"><h2>Mission ${state.mission.number} closed</h2><span class="sub good">ADJUDICATED</span></div><div class="card-body"><h2>${esc(state.mission.aar.outcome)}</h2><p>${esc(state.mission.aar.summary)}</p><button class="button" data-view-jump="history">Open campaign history</button></div></article>`;
  const checked=state.mission.objectives.filter(o=>o.complete).reduce((sum,o)=>sum+o.points,0),tactical=state.mission.tacticalSummary;
  const generated=tactical?`Tactical battle ended after ${tactical.rounds} rounds. Relay observation: ${tactical.observationTurns}/2. Alarm: ${tactical.alarm?'raised':'not raised'}. BLUE effective: ${tactical.effective}/${tactical.starting}.\n\n${tactical.log}`:'';
  return `<article class="card"><div class="card-header"><h2>Mission ${state.mission.number} · ${esc(state.mission.title)}</h2><span class="sub">CURRENT OBJECTIVE SCORE ${checked}</span></div><div class="card-body"><form id="aarForm" class="form-grid">
    <div class="form-field"><label>Outcome</label><select name="outcome"><option>Operational success</option><option>Partial success</option><option>Inconclusive</option><option>Operational setback</option></select></div>
    <div class="form-field"><label>Objective score (0–3)</label><input name="objectiveScore" type="number" min="0" max="3" value="${Math.min(3,Math.ceil(checked/2))}"></div>
    <div class="form-field"><label>KIA</label><input name="kia" type="number" min="0" value="${tactical?.kia||0}"></div><div class="form-field"><label>WIA — Light</label><input name="light" type="number" min="0" value="0"></div>
    <div class="form-field"><label>WIA — Serious</label><input name="serious" type="number" min="0" value="${tactical?.serious||0}"></div><div class="form-field"><label>Critical assets lost</label><input name="assetsLost" type="number" min="0" value="0"></div>
    <div class="form-field full"><label>Mission summary & notable events</label><textarea name="summary" required placeholder="What happened during the tactical battle?">${esc(generated)}</textarea></div>
    <div class="form-field full"><label>Campaign consequences / White Cell notes</label><textarea name="consequences" placeholder="Enemy reaction, control changes, new intelligence, follow-on mission seeds…"></textarea></div>
    <div class="form-field full"><div><button class="button primary" type="submit">Adjudicate & advance campaign</button></div></div>
  </form></div></article>`;
}

function historyView() {
  return `<article class="card"><div class="card-header"><h2>Campaign chronology</h2><span class="sub">NEWEST FIRST</span></div><div>${state.history.map(h=>`<div class="history-item"><div class="history-turn">TURN ${String(h.turn).padStart(2,'0')}</div><div><h3>${esc(h.mission)} · ${esc(h.outcome)}</h3><p>${esc(h.summary)}</p><small style="color:#5f6c64;font:9px var(--mono)">${new Date(h.timestamp).toLocaleString()}</small></div><div class="delta ${momentumClass(h.momentumDelta)}">${h.momentumDelta>0?'+':''}${h.momentumDelta} MOMENTUM</div></div>`).join('')}</div></article>`;
}

function libraryView() {
  const categories = [...new Set(state.library.map(d=>d.category))];
  return `<div class="toolbar"><div><input id="librarySearch" class="search" placeholder="Search ${state.library.length} supplied PDFs…"> <select id="libraryFilter" class="search" style="width:170px"><option value="">All categories</option>${categories.map(c=>`<option>${esc(c)}</option>`).join('')}</select></div><button class="button" id="openLibrary">Open folder</button></div><div id="libraryGrid" class="library-grid">${libraryCards(state.library)}</div><div style="margin-top:18px;color:var(--muted);font:10px/1.6 var(--mono)">Down Range © Nicholas Royer · CC BY-NC-SA 4.0. Campaign layer is an unofficial, non-commercial companion. The complete supplied PDF collection is preserved offline, including archives and research material.</div>`;
}
function libraryCards(items) { return items.map(d=>`<article class="doc"><span class="doc-type">${esc(d.category)}</span><h3>${esc(d.title)}</h3><p>${esc(d.detail)}</p><button class="button open-doc" data-file="${esc(d.file)}">Open PDF</button></article>`).join(''); }

function render() {
  updateHeader();
  const views = { overview:overviewView, mission:missionView, tactical:tacticalView, mcpp:mcppView, forces:forcesView, logistics:logisticsView, intel:intelView, aar:aarView, history:historyView, library:libraryView };
  $('#content').innerHTML = views[currentView]();
  bindView();
}

function openModal(title, fields, onSubmit) {
  $('#modalBody').innerHTML = `<div class="modal-pad"><h2>${esc(title)}</h2><div class="form-grid">${fields.map(f=>`<div class="form-field ${f.full?'full':''}"><label>${esc(f.label)}</label>${f.type==='textarea'?`<textarea name="${f.name}">${esc(f.value||'')}</textarea>`:`<input name="${f.name}" type="${f.type||'text'}" value="${esc(f.value??'')}" ${f.required?'required':''}>`}</div>`).join('')}</div><div class="modal-actions"><button class="button" value="cancel">Cancel</button><button class="button primary" value="default" id="modalSave">Save</button></div></div>`;
  const modal = $('#modal'); modal.showModal();
  $('#modalSave').onclick = (event) => { event.preventDefault(); const data=Object.fromEntries(new FormData($('#modalForm'))); onSubmit(data); modal.close(); };
}

function bindView() {
  document.querySelectorAll('.node').forEach(n=>n.onclick=()=>{selectedLocation=n.dataset.location;render();});
  $('#cycleControl')?.addEventListener('click', (e)=>{const order=['friendly','contested','enemy','unknown'];const loc=state.locations.find(x=>x.id===e.currentTarget.dataset.id);loc.control=order[(order.indexOf(loc.control)+1)%order.length];scheduleSave();render();});
  $('#googleMode')?.addEventListener('click',()=>{mapMode='google';render();});
  $('#schematicMode')?.addEventListener('click',()=>{mapMode='schematic';render();});
  $('#configureMap')?.addEventListener('click',showMapsSettings);
  $('#mapTypeToggle')?.addEventListener('click',()=>{state.campaign.map.type=state.campaign.map.type==='satellite'?'terrain':'satellite';scheduleSave();render();});
  document.querySelectorAll('[data-objective]').forEach(el=>el.onchange=()=>{state.mission.objectives.find(o=>o.id===el.dataset.objective).complete=el.checked;scheduleSave();});
  document.querySelectorAll('[data-plan]').forEach(el=>{
    const update=()=>{if(el.type==='radio'&&!el.checked)return;const value=el.type==='checkbox'?el.checked:el.type==='number'?Number(el.value):el.value;setPath(state.planning,el.dataset.plan,value);scheduleSave();};
    el.addEventListener(el.type==='checkbox'||el.type==='radio'||el.type==='number'?'change':'input',update);
    if(currentView==='mcpp'&&(el.type==='radio'||el.type==='number'))el.addEventListener('change',()=>render());
  });
  document.querySelectorAll('[data-mcpp-step]').forEach(el=>el.onclick=()=>{state.planning.currentStep=Number(el.dataset.mcppStep);scheduleSave();render();});
  $('#mcppBack')?.addEventListener('click',()=>{state.planning.currentStep=clamp(state.planning.currentStep-1,0,5);scheduleSave();render();});
  $('#mcppNext')?.addEventListener('click',()=>{state.planning.currentStep=clamp(state.planning.currentStep+1,0,5);scheduleSave();render();});
  $('#toggleStepComplete')?.addEventListener('click',()=>{const step=Number(state.planning.currentStep);const index=state.planning.completed.indexOf(step);if(index>=0)state.planning.completed.splice(index,1);else state.planning.completed.push(step);state.planning.completed.sort();scheduleSave();render();});
  $('#addWargameRow')?.addEventListener('click',()=>{state.planning.wargame.rows.push({event:'New critical event',action:'',reaction:'',civilian:'',counteraction:''});scheduleSave();render();});
  document.querySelectorAll('.planning-ref').forEach(el=>el.onclick=()=>window.campaignAPI.openPlanningReference(el.dataset.file));
  $('#forceSearch')?.addEventListener('input',(e)=>{const q=e.target.value.toLowerCase();$('#forceRows').innerHTML=forceRows(state.forces.filter(f=>Object.values(f).join(' ').toLowerCase().includes(q)));bindForceRows();});
  bindForceRows();
  $('#addForce')?.addEventListener('click',()=>openForce());
  document.querySelectorAll('.supply-change').forEach(el=>el.onclick=()=>{const item=state.supply.find(s=>s.id===el.dataset.id);item.current=clamp(item.current+Number(el.dataset.delta),0,item.max);scheduleSave();render();});
  $('#addCasualty')?.addEventListener('click',()=>openModal('Record casualty',[{name:'name',label:'Name or identifier',required:true},{name:'unit',label:'Parent unit',required:true},{name:'category',label:'Category (KIA / WIA-L / WIA-S / RTD)',required:true},{name:'returnTurn',label:'Return turn',type:'number'},{name:'note',label:'Notes',full:true}],d=>{state.casualties.push({id:`c${Date.now()}`,name:d.name,unit:d.unit,category:d.category.toUpperCase(),returnTurn:d.returnTurn?Number(d.returnTurn):null,note:d.note});scheduleSave();render();}));
  $('#addIntel')?.addEventListener('click',()=>openModal('Add intelligence report',[{name:'grade',label:'Reliability grade',value:'C3',required:true},{name:'title',label:'Report title',required:true},{name:'confidence',label:'Confidence',value:'Medium'},{name:'age',label:'Age',value:'Current'},{name:'detail',label:'Assessment',type:'textarea',full:true,required:true}],d=>{state.intel.unshift({id:`i${Date.now()}`,...d});scheduleSave();render();}));
  $('#aarForm')?.addEventListener('submit',submitAar);
  document.querySelectorAll('.open-doc').forEach(el=>el.onclick=async()=>{try{await window.campaignAPI.openReference(el.dataset.file);}catch(error){toast(error.message);}});
  $('#openLibrary')?.addEventListener('click',()=>window.campaignAPI.openLibraryFolder());
  const filterLibrary=()=>{const q=($('#librarySearch')?.value||'').toLowerCase(),cat=$('#libraryFilter')?.value||'';$('#libraryGrid').innerHTML=libraryCards(state.library.filter(d=>(!cat||d.category===cat)&&Object.values(d).join(' ').toLowerCase().includes(q)));document.querySelectorAll('.open-doc').forEach(el=>el.onclick=()=>window.campaignAPI.openReference(el.dataset.file));};
  $('#librarySearch')?.addEventListener('input',filterLibrary);$('#libraryFilter')?.addEventListener('change',filterLibrary);
  document.querySelectorAll('[data-view-jump]').forEach(el=>el.onclick=()=>switchView(el.dataset.viewJump));
  if(currentView==='tactical')bindTacticalView();
  if(currentView==='overview'&&mapMode==='google'&&mapsReady) renderGoogleMap();
}

function bindTacticalView(){
  const t=state.tactical;
  const chooseUnit=id=>{const clicked=tacticalUnit(id),selected=tacticalUnit(t.selectedId);if(selected&&clicked.id!==selected.id&&(clicked.side!==selected.side||clicked.status==='downed'))t.targetId=clicked.id;else t.selectedId=clicked.id;render();};
  document.querySelectorAll('[data-roster-unit]').forEach(el=>el.onclick=()=>{t.selectedId=el.dataset.rosterUnit;render();});
  document.querySelectorAll('[data-tactical-unit]').forEach(el=>{
    el.onclick=()=>chooseUnit(el.dataset.tacticalUnit);
    el.onpointerdown=event=>{
      const unit=tacticalUnit(el.dataset.tacticalUnit);
      if(unit.side!==t.activeSide||unit.moved||unit.focused||['downed','dead'].includes(unit.status))return;
      event.preventDefault();el.setPointerCapture?.(event.pointerId);
      tacticalDrag={unit,el,startX:unit.x,startY:unit.y,board:$('#tacticalBoard').getBoundingClientRect(),moved:false};
    };
  });
  document.onpointermove=event=>{if(!tacticalDrag)return;const d=tacticalDrag,x=clamp((event.clientX-d.board.left)/d.board.width*100,1,99),y=clamp((event.clientY-d.board.top)/d.board.height*100,1,99);d.el.style.left=`${x}%`;d.el.style.top=`${y}%`;d.x=x;d.y=y;d.moved=true;};
  document.onpointerup=()=>{if(!tacticalDrag)return;const d=tacticalDrag;tacticalDrag=null;if(!d.moved)return;const destination={x:d.x,y:d.y},distance=TacticalRules.distanceInches({x:d.startX,y:d.startY},destination),allowance=TacticalRules.movementAllowance(d.unit,{sprint:d.unit.sprint,impaired:t.impairedMovement});if(distance>allowance+.05){toast(`Move is ${distance.toFixed(1)}\"; allowance is ${allowance.toFixed(1)}\".`);render();return;}d.unit.x=destination.x;d.unit.y=destination.y;d.unit.moved=true;tacticalLog(`${d.unit.name} moves ${distance.toFixed(1)}\"${d.unit.sprint?' at a sprint':''}${t.impairedMovement?' through impaired terrain':''}.`,'move');scheduleSave();render();};
  $('#tacticalCover').onchange=e=>{t.cover=e.target.value;scheduleSave();};
  $('#tacticalTerrain').onchange=e=>{t.impairedMovement=e.target.value==='impaired';scheduleSave();render();};
  $('#tacticalFire')?.addEventListener('click',()=>executeTacticalAttack(false));
  $('#tacticalSuppress')?.addEventListener('click',()=>executeTacticalAttack(true));
  $('#tacticalReaction')?.addEventListener('click',()=>{const u=tacticalUnit(t.selectedId);if(!tacticalCanUseAction(u))return;u.actionUsed=true;u.reaction=true;tacticalLog(`${u.name} holds an action as a reaction.`);scheduleSave();render();});
  $('#tacticalSprint')?.addEventListener('click',()=>{const u=tacticalUnit(t.selectedId);if(!tacticalCanUseAction(u)||u.kind!=='troop')return;u.actionUsed=true;u.sprint=true;tacticalLog(`${u.name} sacrifices its action to sprint.`,'move');scheduleSave();render();});
  $('#tacticalFocus')?.addEventListener('click',()=>{const u=tacticalUnit(t.selectedId);if(!tacticalCanUseAction(u))return;consumeTacticalAction(u);u.focused=true;u.moved=true;tacticalLog(`${u.name} focuses and gives up movement.`);scheduleSave();render();});
  $('#tacticalObserve')?.addEventListener('click',()=>{const u=tacticalUnit(t.selectedId),target=tacticalUnit(t.targetId);if(!tacticalCanUseAction(u)||!u.radio||!target)return;consumeTacticalAction(u);u.signaled=true;target.observedBy=u.side;target.observedRound=t.round;tacticalLog(`${u.name} signals fires observation on ${target.name}; friendly attacks gain advantage.`,'signal');scheduleSave();render();});
  $('#tacticalTreat')?.addEventListener('click',executeMedicine);
  $('#observeRelay')?.addEventListener('click',observeRelay);
  $('#endTacticalTurn')?.addEventListener('click',endTacticalTurn);
  $('#finishTactical')?.addEventListener('click',finishTacticalMission);
  $('#tacticalRules')?.addEventListener('click',()=>window.campaignAPI.openReference('DownRangeLatest/Rules Compressed-278da66fbe36c91eae0252e2830de80b.pdf'));
  $('#launchUnityBattle')?.addEventListener('click',async()=>{try{const result=await window.campaignAPI.launchUnityBattle(state);state.unityBattle=result.unityBattle;render();toast(result.resumed?'Unity battle resumed':'Unity tactical resolver launched');}catch(error){toast(error.message);}});
  $('#launchUnityOneStar')?.addEventListener('click',async()=>{try{await window.campaignAPI.launchUnityOneStar();toast('One Star 3D tabletop launched');}catch(error){toast(error.message);}});
  $('#importUnityBattle')?.addEventListener('click',async()=>{try{const result=await window.campaignAPI.importUnityResult(state);if(!result.ready){toast(result.message);return;}state=result.state;normalizeTacticalData();render();toast(result.alreadyImported?'Result was already imported':'Unity result imported · AAR ready');}catch(error){toast(error.message);}});
  $('#resetTactical')?.addEventListener('click',async()=>{if(t.committed)return toast('Results are already committed; reset the campaign save to replay.');if(!confirm('Reset the in-app tactical battle, discard any pending Unity battle, and reset One Star 3D to Round 1? Close any running Unity tactical window first.'))return;try{await window.campaignAPI.resetUnityState(state);delete state.tactical;delete state.unityBattle;normalizeTacticalData();scheduleSave();render();toast('Electron tactical battle and Unity 3D saves reset.');}catch(error){toast(error.message);}});
}

function bindForceRows(){document.querySelectorAll('.force-row').forEach(row=>row.onclick=()=>openForce(state.forces.find(f=>f.id===row.dataset.id)));}
function openForce(existing) {
  const f=existing||{};
  openModal(existing?'Edit force element':'Add force element',[
    {name:'name',label:'Element name',value:f.name,required:true},{name:'type',label:'Type',value:f.type||'Infantry'},
    {name:'current',label:'Current strength',type:'number',value:f.current??0},{name:'authorized',label:'Authorized strength',type:'number',value:f.authorized??0},
    {name:'readiness',label:'Readiness %',type:'number',value:f.readiness??100},{name:'ammo',label:'Critical ammo',value:f.ammo||'Full'},
    {name:'assets',label:'Special assets',value:f.assets,full:true},{name:'status',label:'Status',value:f.status||'Available',full:true}
  ],d=>{const item={id:f.id||`f${Date.now()}`,name:d.name,type:d.type,current:Number(d.current),authorized:Number(d.authorized),readiness:clamp(Number(d.readiness),0,100),ammo:d.ammo,assets:d.assets,status:d.status};if(existing)Object.assign(existing,item);else state.forces.push(item);scheduleSave();render();});
}

function submitAar(event) {
  event.preventDefault(); const report=Object.fromEntries(new FormData(event.target));
  ['objectiveScore','kia','light','serious','assetsLost'].forEach(k=>report[k]=Number(report[k]||0));
  const losses=report.kia+report.serious; const delta=clamp(report.objectiveScore-Math.ceil(losses/2)-report.assetsLost,-2,2);
  state.campaign.momentum=clamp(state.campaign.momentum+delta,-3,3); state.campaign.turn+=1; state.campaign.phase='Campaign update';
  state.history.unshift({id:`h${Date.now()}`,turn:state.campaign.turn-1,mission:state.mission.title,outcome:report.outcome,summary:report.summary,momentumDelta:delta,timestamp:new Date().toISOString()});
  state.mission.status='complete'; state.mission.aar=report; scheduleSave(); render(); toast(`Campaign advanced · momentum ${delta>=0?'+':''}${delta}`);
}

function switchView(view) {
  currentView=view; document.querySelectorAll('.nav-item').forEach(el=>el.classList.toggle('active',el.dataset.view===view)); render();
}

document.querySelectorAll('.nav-item').forEach(el=>el.addEventListener('click',()=>switchView(el.dataset.view)));
$('#exportBtn').onclick=async()=>{const result=await window.campaignAPI.exportCampaign(state);if(!result.canceled)toast('Campaign exported');};
$('#importBtn').onclick=async()=>{try{const result=await window.campaignAPI.importCampaign();if(!result.canceled){state=result.state;normalizeMapData();normalizePlanningData();normalizeTacticalData();render();toast('Campaign imported');}}catch(error){toast(`Import failed: ${error.message}`);}};
$('#mapsKeyBtn').onclick=showMapsSettings;

window.campaignAPI.load().then(async(loaded)=>{
  state=loaded;normalizeMapData();normalizePlanningData();normalizeTacticalData();render();
  try{unityStatusData=await window.campaignAPI.getUnityStatus();render();}catch{}
  const key=await window.campaignAPI.getMapsKey();
  if(key){try{await loadGoogleMaps(key);mapMode='google';render();}catch(error){toast(error.message);}}
}).catch((error)=>{$('#content').innerHTML=`<div class="empty">Unable to load campaign: ${esc(error.message)}</div>`;});
