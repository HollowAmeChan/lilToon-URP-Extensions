"""Static figure of actual GPU stage observations; not rendered scene imagery."""
from pathlib import Path
import json
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np
HERE=Path(__file__).resolve().parent
r=json.loads((HERE/'analysis.json').read_text(encoding='utf-8-sig'));rows=r['new18StageError'][4:];x=np.arange(5);labels=['First / 1','Step 2','Step 4','Step 8','Step 16']
fig,axes=plt.subplots(2,2,figsize=(10.6,6.8),layout='constrained');colors=['#285EAD','#D0602D']
ax=axes[0,0]
for c,key,label in zip(colors,['new18BaselineReferenceL1','new18DIReferenceL1'],['Stock','DI-derived (rejected)']):ax.plot(x,[s[key] for s in rows],'-o',color=c,label=label)
ax.set_title('18 bright-edge receivers: normalized L1');ax.set_ylabel('Lower is better');ax.legend(fontsize=8)
for ax,p in zip([axes[0,1],axes[1,0]],r['oldFailureStageTrajectories']):
 for c,key,label in zip(colors,['baselineOverReference','diOverReference'],['Stock','DI-derived']):ax.plot(x,[s[key][2] for s in p['stages'][4:]],'-o',color=c,label=label)
 ax.axhline(1,color='#555555',linestyle='--',linewidth=1);ax.set_title(f"Old control ({p['x']}, {p['y']}): blue / reference");ax.set_ylabel('Reference = 1')
ax=axes[1,1];p=r['oldFailureStageTrajectories'][0]
for c,key,label in zip(colors,['baselineVariance','diVariance'],['Stock','DI-derived']):ax.plot(x[:4],[s[key]['mean'] for s in p['stages'][4:8]],'-o',color=c,label=label)
ax.set_yscale('log');ax.set_title('Control (141,157): actual variance');ax.set_ylabel('NRD demodulated units squared');ax.annotate('Final alpha stores history length',xy=(.04,.07),xycoords='axes fraction',fontsize=8)
for ax in axes.ravel():ax.set_xticks(x,labels,fontsize=8);ax.grid(alpha=.2)
fig.suptitle('Same GI inputs, independent NRD branches: 4 seeds x 256 observed frames',fontsize=12)
fig.savefig(HERE/'stage-trajectories.png',dpi=160);plt.close(fig)
print('Saved stage-trajectories.png from actual paired-stage statistics')
