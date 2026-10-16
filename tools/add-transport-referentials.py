# -*- coding: utf-8 -*-
"""Ajoute les référentiels transport (lCarriers, lTerminals) au jeu de seed BorderNext.

Source : nomenclatures de référence du poste frontière (carriers.tsv,
terminals.tsv). Les terminaux sont restreints au sous-ensemble opérationnel
(liés à une ville, hors entrée de test VNR/OOO) ; les 822 compagnies sont reprises
intégralement (codes IATA hérités).
"""
import io
import json

BASE = 'referentials'
SEED = 'src/BorderNext.Infrastructure/Persistence/Seeding/reference-data.json'

def read_tsv(path):
    rows = []
    for line in io.open(path, encoding='utf-8').read().splitlines():
        if not line.strip():
            continue
        rows.append(line.split('#~#'))
    return rows

carriers = []
for old_id, code, ttype, alpha3, sort_order, fr, en in read_tsv(BASE + '/carriers.tsv'):
    carriers.append({
        'oldId': int(old_id), 'code': code, 'type': ttype,
        'countryAlpha3': alpha3, 'sortOrder': int(sort_order), 'fr': fr, 'en': en,
    })

terminals = []
for old_id, code, ttype, alpha3, city_code, sort_order, fr, en in read_tsv(BASE + '/terminals.tsv'):
    terminals.append({
        'oldId': int(old_id), 'code': code, 'type': ttype,
        'countryAlpha3': alpha3, 'cityCode': city_code, 'sortOrder': int(sort_order), 'fr': fr, 'en': en,
    })

data = json.load(io.open(SEED, encoding='utf-8-sig'))
data['carriers'] = carriers
data['terminals'] = terminals
data['note'] = data.get('note', '') + (
    ' | Lot 6 : carriers (822 compagnies, codes IATA hérités) et terminals '
    '(18 terminaux opérationnels liés à une ville ; le dump hérité compte ~10 775 aéroports, '
    'gérables ensuite depuis l’administration des terminaux).'
)
io.open(SEED, 'w', encoding='utf-8').write(json.dumps(data, ensure_ascii=False, indent=1))
print('carriers:', len(carriers), 'terminals:', len(terminals))
