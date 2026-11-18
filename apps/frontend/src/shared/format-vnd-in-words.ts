const DIGIT_WORDS = [
  'không',
  'một',
  'hai',
  'ba',
  'bốn',
  'năm',
  'sáu',
  'bảy',
  'tám',
  'chín',
] as const;

// Sáu nhóm ba chữ số đủ phủ tới `Number.MAX_SAFE_INTEGER` (16 chữ số).
const SCALE_NAMES = ['', 'nghìn', 'triệu', 'tỷ', 'nghìn tỷ', 'triệu tỷ'] as const;

function readUnitWord(tens: number, units: number, hasHundredsPrefix: boolean): string {
  // Sau hàng chục từ 2 trở lên, chữ số 1 đọc "mốt" theo chuẩn tiếng Việt.
  if (units === 1 && tens >= 2) {
    return 'mốt';
  }

  // Sau "mươi" hoặc "linh", chữ số 4 đọc "tư". Đứng đầu hoặc sau "mười" vẫn giữ "bốn".
  if (units === 4 && (tens >= 2 || (tens === 0 && hasHundredsPrefix))) {
    return 'tư';
  }

  // Khi hàng chục từ 1 trở lên, chữ số 5 đọc "lăm". Đứng đầu hoặc sau "linh" vẫn giữ "năm".
  if (units === 5 && tens >= 1) {
    return 'lăm';
  }

  return DIGIT_WORDS[units]!;
}

function readThreeDigitGroup(groupValue: number, isHighestGroup: boolean): string {
  const hundreds = Math.floor(groupValue / 100);
  const tens = Math.floor((groupValue % 100) / 10);
  const units = groupValue % 10;
  const parts: string[] = [];

  // Nhóm thấp hơn vẫn đọc "không trăm" khi hàng trăm bằng 0 để giữ rõ vị trí hàng. Nhóm cao nhất bỏ "không trăm".
  const hasHundredsPrefix = hundreds > 0 || !isHighestGroup;
  if (hasHundredsPrefix) {
    parts.push(`${DIGIT_WORDS[hundreds]!} trăm`);
  }

  // Hàng chục bằng 1 đọc "mười" thay cho "một mươi".
  if (tens === 1) {
    parts.push('mười');
  } else if (tens >= 2) {
    parts.push(`${DIGIT_WORDS[tens]!} mươi`);
  } else if (units > 0 && hasHundredsPrefix) {
    // Chữ "linh" chỉ xuất hiện khi có cụm "trăm" đứng trước và hàng chục bằng 0.
    parts.push('linh');
  }

  if (units > 0) {
    parts.push(readUnitWord(tens, units, hasHundredsPrefix));
  }

  return parts.join(' ');
}

function splitIntoThreeDigitGroups(amount: number): number[] {
  const groups: number[] = [];
  let remaining = amount;

  // Tách từ phải sang trái thành từng nhóm ba chữ số để ghép với bảng tên bậc theo chỉ số nhóm.
  while (remaining > 0) {
    groups.push(remaining % 1000);
    remaining = Math.floor(remaining / 1000);
  }

  return groups;
}

function joinScaleGroups(groups: number[]): string {
  const highestIndex = groups.length - 1;
  const segments: string[] = [];

  for (let groupIndex = highestIndex; groupIndex >= 0; groupIndex -= 1) {
    const groupValue = groups[groupIndex]!;
    // Bỏ qua nhóm toàn số 0 cùng tên bậc của nhóm đó để tránh cụm thừa như "không triệu".
    if (groupValue === 0) {
      continue;
    }

    const isHighestGroup = groupIndex === highestIndex;
    segments.push(readThreeDigitGroup(groupValue, isHighestGroup));

    const scaleName = SCALE_NAMES[groupIndex];
    if (scaleName) {
      segments.push(scaleName);
    }
  }

  return segments.join(' ');
}

// Đổi số tiền nguyên đồng sang chữ tiếng Việt theo chuẩn hóa đơn.
export function formatVndInWords(amount: number): string {
  // Tổng tiền của đơn luôn là số nguyên không âm trong miền an toàn của JavaScript. Số thực, số âm hoặc vô cực là dữ liệu sai.
  if (!Number.isInteger(amount) || amount < 0 || amount > Number.MAX_SAFE_INTEGER) {
    throw new RangeError(
      `Số tiền phải là số nguyên từ 0 đến ${Number.MAX_SAFE_INTEGER}, nhận được: ${amount}.`,
    );
  }

  // Giá trị 0 (gồm cả `-0`) trả sớm vì vòng tách nhóm dừng ngay khi số bằng 0.
  if (amount === 0) {
    return 'Không đồng';
  }

  const groups = splitIntoThreeDigitGroups(amount);
  const words = joinScaleGroups(groups);
  // Viết hoa đúng ký tự đầu câu để hiển thị trực tiếp trên dòng tổng tiền của hóa đơn.
  return `${words.charAt(0).toUpperCase()}${words.slice(1)} đồng`;
}
