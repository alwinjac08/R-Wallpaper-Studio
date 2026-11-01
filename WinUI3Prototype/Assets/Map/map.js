/* Local Leaflet renderer. Only visible map tiles are requested from OpenStreetMap. */
(() => {
  'use strict';
  const testMode=new URLSearchParams(location.search).get('test')==='1';
  const add=document.getElementById('add'), hint=document.getElementById('hint');
  const post=message=>window.chrome?.webview?.postMessage(message);
  const map=L.map('map',{zoomControl:true,attributionControl:false,scrollWheelZoom:true,minZoom:2,maxZoom:19,worldCopyJump:true}).setView([20,0],2);
  L.control.attribution({position:'bottomleft',prefix:false}).addTo(map)
    .addAttribution('© <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a> contributors');
  let tiles=null, tileError=false;
  if(!testMode) {
    tiles=L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',{maxZoom:19,keepBuffer:1,updateWhenIdle:true}).addTo(map);
    tiles.on('tileerror',()=>{tileError=true;hint.textContent='Map tiles unavailable. Check your internet connection.';});
    tiles.on('tileload',()=>{if(tileError){tileError=false;hint.textContent='Click a place to drop a pin.';}});
    window.addEventListener('online',()=>tiles.redraw());
    // Network switches do not always raise an online event in an embedded browser.
    let retryDelay=15000;
    function retryTiles(){
      if(tileError && navigator.onLine){tiles.redraw();retryDelay=Math.min(60000,retryDelay*2);}
      else retryDelay=15000;
      setTimeout(retryTiles,retryDelay);
    }
    setTimeout(retryTiles,retryDelay);
  }
  new ResizeObserver(()=>map.invalidateSize({pan:false})).observe(document.getElementById('map'));
  let current=null,pending=null,currentData=null,pendingData=null,saved=[],savedMarkers=[],interacted=false,centered=false,saving=false,selectedId=null;
  let currentFocus=true;
  function action(remove,enabled){
    add.style.display='grid';
    add.disabled=!enabled;add.title=remove?'Remove saved location':'Save selected location';
    add.setAttribute('aria-label',add.title);add.style.background=remove?'#A6083F':'';
    add.innerHTML=remove?'<span style="font-size:11px;color:white">Remove</span>':'<img src="Add.svg" width="24" height="24" alt="">';
  }
  function icon(color){return L.divIcon({className:'studio-pin',html:'<span class="pin" style="--pin-color:'+color+'"></span>',iconSize:[30,36],iconAnchor:[15,30],popupAnchor:[0,-30]});}
  function valid(p){return p&&Number.isFinite(p.latitude)&&Number.isFinite(p.longitude)&&p.latitude>=-90&&p.latitude<=90&&p.longitude>=-180&&p.longitude<=180;}
  function pointLabel(p){return p.latitude.toFixed(5)+', '+p.longitude.toFixed(5);}
  function near(a,b){
    if(!a||!b)return false;
    const rad=Math.PI/180,lat=(a.latitude-b.latitude)*rad,lon=(a.longitude-b.longitude)*rad;
    const h=Math.sin(lat/2)**2+Math.cos(a.latitude*rad)*Math.cos(b.latitude*rad)*Math.sin(lon/2)**2;
    return 6371000*2*Math.asin(Math.sqrt(Math.min(1,Math.max(0,h))))<1;
  }
  function currentSaved(){return saved.find(p=>near(p,currentData));}
  function syncCurrent(){
    if(!currentData)return;
    const match=currentSaved();
    // The saved marker represents this place; do not overlay it with a blue pin.
    if(match){if(current)current.remove();current=null;}
    else {
      if(current)current.setLatLng([currentData.latitude,currentData.longitude]);
      else {
        current=L.marker([currentData.latitude,currentData.longitude],{icon:icon('#082FA6'),zIndexOffset:1000,title:'Current location',bubblingMouseEvents:false}).addTo(map).bindPopup('Your current location');
        current.on('click',()=>{if(saving)return;currentFocus=true;syncCurrent();});
      }
    }
    if(!currentFocus||saving)return;
    if(pending)pending.remove();pending=null;selectedId=null;pendingData=null;
    if(match){action(false,false);add.style.display='none';hint.textContent='Your current location is saved. Click its red pin to remove it.';}
    else {
      pendingData={...currentData};action(false,false);
      hint.textContent='Your current location is not saved. Click + to add it.';
      post({type:'pick',...pendingData});
    }
  }
  function candidate(latitude,longitude){
    const p={latitude,longitude:((longitude+180)%360+360)%360-180}; if(!valid(p)||saving)return;
    interacted=true;currentFocus=false;pendingData=p;selectedId=null;action(false,false);
    if(pending) pending.setLatLng([p.latitude,p.longitude]);
    else {
      pending=L.marker([p.latitude,p.longitude],{icon:icon('#6B7280'),draggable:true,zIndexOffset:1500,title:'Selected location'}).addTo(map);
      pending.on('dragend',()=>{const v=pending.getLatLng();candidate(v.lat,v.lng);});
    }
    add.disabled=true;hint.textContent='Selected '+pointLabel(p);post({type:'pick',...p});
  }
  map.on('click',e=>candidate(e.latlng.lat,e.latlng.lng));
  map.on('dragstart',()=>{interacted=true;});map.on('zoomstart',()=>{interacted=true;});
  document.getElementById('locate').addEventListener('click',()=>post({type:'locate'}));
  add.addEventListener('click',()=>{if(add.disabled)return;if(!selectedId&&!pendingData)return;saving=true;add.disabled=true;post({type:selectedId?'remove':'save'});});
  function receive(m){
    if(m.type==='locations'){
      const selectedWasCurrent=selectedId&&saved.some(p=>p.id===selectedId&&near(p,currentData));
      const savingCurrent=pendingData&&near(pendingData,currentData);
      savedMarkers.forEach(marker=>marker.remove());savedMarkers=[];saved=m.locations.filter(valid);
      saved.forEach(p=>{
        const label=document.createElement('div');label.textContent=p.name+' · '+pointLabel(p);
        const marker=L.marker([p.latitude,p.longitude],{icon:icon('#A6083F'),zIndexOffset:500,title:p.name,bubblingMouseEvents:false}).addTo(map).bindPopup(label);
        marker.on('click',()=>{if(!saving)post({type:'select',id:p.id});});savedMarkers.push(marker);
      });
      if(m.initial&&saved.length&&!currentData&&!interacted){map.setView([saved[0].latitude,saved[0].longitude],13);interacted=false;}
      if(m.clearPending){if(pending)pending.remove();pending=null;pendingData=null;selectedId=null;saving=false;action(false,false);hint.textContent='Click another place to add.';}
      if(m.clearPending&&(selectedWasCurrent||savingCurrent))currentFocus=true;
      syncCurrent();
    } else if(m.type==='selected'){
      currentFocus=false;
      if(pending)pending.remove();pending=null;pendingData=null;selectedId=m.id;saving=false;
      action(true,m.canRemove);hint.textContent='Saved place selected. Click Remove to delete it.';
    } else if(m.type==='candidate'){
      if(m.duplicate){if(pending)pending.remove();pending=null;pendingData=null;}
      saving=false;selectedId=null;action(false,m.canAdd);
      if(m.message)hint.textContent=m.message;
    } else if(m.type==='current'&&valid(m)){
      currentData={latitude:m.latitude,longitude:m.longitude};
      if(m.center)currentFocus=true;
      syncCurrent();
      if(m.center||(!centered&&!interacted)){map.setView([m.latitude,m.longitude],15);centered=true;interacted=false;}
    } else if(m.type==='locationUnavailable'){
      if(current)current.remove();current=null;currentData=null;
      if(currentFocus){pendingData=null;action(false,false);}
      if(!pendingData)hint.textContent=m.message;
    } else if(m.type==='status'&&!pendingData){hint.textContent=m.message;}
  }
  window.chrome.webview.addEventListener('message',e=>receive(e.data));
  new ResizeObserver(()=>map.invalidateSize({pan:false})).observe(document.getElementById('map'));
  if(testMode) window.studioMapTest={
    click:(lat,lng)=>map.fire('click',{latlng:L.latLng(lat,lng)}),
    pan:(x,y)=>map.panBy([x,y],{animate:false}),
    select:()=>savedMarkers[0]?.fire('click'),
    inspect:()=>({current:currentData,pending:pendingData,saved:saved,canAdd:!add.disabled&&!selectedId,canRemove:!add.disabled&&!!selectedId,actionVisible:add.style.display!=='none',currentColor:currentSaved()?'#A6083F':current?'#082FA6':null,bluePinCount:current?1:0,savedColors:savedMarkers.map(()=> '#A6083F'),center:map.getCenter(),dragging:map.dragging.enabled()}),
    save:()=>add.click()
  };
  post({type:'ready'});
})();
