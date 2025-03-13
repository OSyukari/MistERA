// Reference: https://gitgud.io/umaera/engine/era-electron/-/wikis/dev-guides/20-updates#%E8%87%AA%E5%8A%A8%E6%9E%84%E5%BB%BA%E6%B8%B8%E6%88%8F%E7%89%88%E6%9C%AC%E5%B9%B6%E5%8F%91%E5%B8%83

// ci/build.js
const { execSync } = require('child_process');
const {
    createWriteStream,
    mkdirSync,
    readFileSync,
    readdirSync,
    renameSync,
    rmSync,
    statSync,
    writeFileSync,
  } = require('fs');  
const { join, resolve } = require('path');
const { path7za } = require('7zip-bin');
const { zip } = require('compressing');
const { copySync } = require('fs-extra');

const work_dir = resolve('.'),
  game_dir = join(process.env.GAME_DIR),
  game_zip = join(process.env.GAME_DIR, process.env.GAME_ZIP);

// 生成PC游戏包并上传
rmSync(game_dir, { recursive: true, force: true });
mkdirSync(game_dir);
// PC游戏包只有 ere 和 csv 两个文件夹，可以把 csv 换成 json 或 yml
['MistEra'].forEach((d) => copySync(join(work_dir, d), join(game_dir, d)));
rmSync(game_zip, { force: true });
/*
switch (os.platform()) {
  case 'linux':
  case 'darwin':
    execSync(`chmod +x ${path7za}`);
    break;
  case 'win32':
} */
execSync(`${path7za} a -pera ${game_zip} ${game_dir}`);
execSync(
  `curl --fail-with-body --header "JOB-TOKEN: ${process.env.CI_JOB_TOKEN}" --upload-file ${game_zip} "${process.env.GAME_URL}"`,
);
// 创建最小更新包
// ...
