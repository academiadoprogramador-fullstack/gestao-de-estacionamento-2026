#!/usr/bin/env node
// Executa `ng test` aceitando também `--run`, usado na documentação da fase,
// como sinônimo de execução única (sem watch).
import { spawnSync } from 'node:child_process';

const argumentos = process.argv.slice(2).filter((argumento) => argumento !== '--run');

const resultado = spawnSync(
  process.execPath,
  ['node_modules/@angular/cli/bin/ng.js', 'test', ...argumentos],
  { stdio: 'inherit' },
);

process.exit(resultado.status ?? 1);
