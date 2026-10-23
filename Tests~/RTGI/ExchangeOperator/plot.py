from pathlib import Path
HERE=Path(__file__).resolve().parent
import sys,json,hashlib
local=HERE.parents[2]/'.codex-research/rtgi-plot-deps'
if local.exists():sys.path.insert(0,str(local))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np
r=json.loads((HERE/'analysis.json').read_text(encoding='utf-8-sig'));fig,axs=plt.subplots(1,2,figsize=(11,4.4),layout='constrained');x=np.arange(2);groups=[r['groups']['legacy'],r['groups']['new']]
for i,(key,name,c) in enumerate([('baselineNormalizedL1','Stock','#285EAD'),('diReferenceNormalizedL1','DI reference (rejected)','#D0602D'),('candidateNormalizedL1','Row normalization (rejected)','#3D865C')]):
 values=[s[key] for s in groups];bars=axs[0].bar(x+(i-1)*.24,values,.24,label=name,color=c);axs[0].bar_label(bars,fmt='%.3f',padding=3,fontsize=8)
axs[0].set_xticks(x,['Old 15 qualified receivers','New 18 bright-edge receivers'],fontsize=9);axs[0].set_ylim(0,.54);axs[0].set_ylabel('Normalized reference L1 (lower is better)');axs[0].set_title('Most DI aggregate gain disappears');axs[0].legend(fontsize=8,loc='upper left');axs[0].grid(axis='y',alpha=.2)
values=[p['fractionOfDiRegressionRecovered']*100 for p in r['mechanismRecovery']];bars=axs[1].bar(range(3),values,color='#3D865C',width=.6);axs[1].bar_label(bars,fmt='%.1f%%',padding=3);axs[1].set_xticks(range(3),['(141,157) G','(141,157) B','(196,141) B'],fontsize=9);axs[1].axhline(50,color='#555555',linestyle='--',label='Predeclared mechanism prediction');axs[1].set_ylim(0,110);axs[1].set_ylabel('DI-induced error recovered (%)');axs[1].set_title('Old controls recover with row normalization');axs[1].legend(fontsize=8,loc='lower left');axs[1].grid(axis='y',alpha=.2)
fig.suptitle('Same-input single-operator ablation: 4 seeds x 256 observed frames',fontsize=12);fig.savefig(HERE/'operator-tradeoff.png',dpi=160);plt.close(fig)
meta={'dataFigureNotSceneScreenshot':True,'sourceAnalysisSha256':hashlib.sha256((HERE/'analysis.json').read_bytes()).hexdigest().upper(),'figureSha256':hashlib.sha256((HERE/'operator-tradeoff.png').read_bytes()).hexdigest().upper(),'matplotlibVersion':matplotlib.__version__,'scope':'Descriptive four-seed means, no formal CI; reused isolated plotting dependencies from038; no new package installation'}
(HERE/'figure.json').write_text(json.dumps(meta,indent=2)+'\n',encoding='utf-8');print('Saved operator-tradeoff.png')
