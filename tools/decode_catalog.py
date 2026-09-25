import json, base64, struct, sys
d = json.load(open(sys.argv[1], encoding='utf-8-sig'))
kd = base64.b64decode(d['m_KeyDataString'])
bd = base64.b64decode(d['m_BucketDataString'])
ed = base64.b64decode(d['m_EntryDataString'])
ids = d['m_InternalIds']
provs = d['m_ProviderIds']
types = [t['m_ClassName'] for t in d['m_resourceTypes']]

def read_key(off):
    t = kd[off]; off += 1
    if t in (0, 1):
        n = struct.unpack_from('<i', kd, off)[0]; off += 4
        return kd[off:off+n].decode('ascii' if t == 0 else 'utf-16-le', 'replace')
    if t == 2: return struct.unpack_from('<H', kd, off)[0]
    if t == 3: return struct.unpack_from('<I', kd, off)[0]
    if t == 4: return struct.unpack_from('<i', kd, off)[0]
    if t == 5:
        n = kd[off]; return kd[off+1:off+1+n].decode('ascii', 'replace')
    return f'<type{t}>'

bc = struct.unpack_from('<i', bd, 0)[0]
buckets = []
o = 4
for _ in range(bc):
    doff, ec = struct.unpack_from('<ii', bd, o); o += 8
    ents = struct.unpack_from(f'<{ec}i', bd, o); o += 4 * ec
    buckets.append((doff, ents))
keys = [read_key(b[0]) for b in buckets]

ec = struct.unpack_from('<i', ed, 0)[0]
entries = [struct.unpack_from('<7i', ed, 4 + i * 28) for i in range(ec)]

def row(e):
    iid, prov, depk, deph, data, pk, rt = e
    return ids[iid], provs[prov], keys[pk] if 0 <= pk < len(keys) else pk, types[rt] if 0 <= rt < len(types) else rt, keys[depk] if depk >= 0 else ''

out = open(sys.argv[2], 'w', encoding='utf-8')
out.write('key\tprimaryKey\tinternalId\ttype\tprovider\tdependencyKey\n')
for k, (doff, ents) in zip(keys, buckets):
    for ei in ents:
        iid, prov, pk, rt, dep = row(entries[ei])
        out.write(f'{k}\t{pk}\t{iid}\t{rt}\t{prov.split(".")[-1]}\t{dep}\n')
out.close()
print(len(keys), 'keys', ec, 'entries')
